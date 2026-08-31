using System.Data;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Beeexy.Infrastructure.Scheduling;

internal sealed class AppointmentRequestTransaction(
    BeeexyDbContext dbContext,
    PublicDirectoryQueryBoundary publicDirectory) : IAppointmentRequestTransaction
{
    private const string IdempotencyConstraint =
        "ux_appointments_account_idempotency_key";
    private const string ReservingSlotConstraint =
        "ux_appointments_reserving_slot";

    private IDbContextTransaction? transaction;

    public async Task BeginAsync(
        EntityId requestingAccountId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException(
                "The appointment request transaction is already active.");
        }

        transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var lockKey = $"appointment-request:{requestingAccountId.Value:D}:{idempotencyKey.Value:D}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 83));",
            cancellationToken);
    }

    public Task<AppointmentRequestState?> FindExistingAsync(
        EntityId requestingAccountId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default) =>
        FindExistingCoreAsync(requestingAccountId, idempotencyKey, cancellationToken);

    public async Task<AppointmentSlotRequestState?> FindSlotAsync(
        EntityId slotId,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionActive();
        var publicClinics = publicDirectory.Clinics();
        var publicLocations = publicDirectory.ClinicLocations();
        var publicDoctors = publicDirectory.Doctors();
        var publicAffiliations = publicDirectory.DoctorAffiliations();

        return await dbContext.AvailabilitySlots
            .AsNoTracking()
            .Where(slot => slot.Id == slotId)
            .Select(slot => new AppointmentSlotRequestState(
                slot,
                publicDoctors.Any(doctor => doctor.Id == slot.DoctorId) &&
                publicClinics.Any(clinic => clinic.Id == slot.ClinicId) &&
                publicLocations.Any(location =>
                    location.Id == slot.ClinicLocationId &&
                    location.ClinicId == slot.ClinicId) &&
                publicAffiliations.Any(affiliation =>
                    affiliation.DoctorId == slot.DoctorId &&
                    affiliation.ClinicId == slot.ClinicId &&
                    affiliation.ClinicLocationId == slot.ClinicLocationId)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public void Add(Appointment appointment)
    {
        EnsureTransactionActive();
        dbContext.Appointments.Add(appointment);
    }

    public async Task<AppointmentRequestSaveResult> SaveAsync(
        Appointment appointment,
        AvailabilitySlot slot,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionActive();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AppointmentRequestSaveResult(
                new AppointmentRequestState(appointment, slot),
                NewlyCreated: true);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(exception, ReservingSlotConstraint))
        {
            await RollbackAndClearAsync(cancellationToken);
            throw new AppointmentSlotReservationConflictException();
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(exception, IdempotencyConstraint))
        {
            await RollbackAndClearAsync(cancellationToken);
            var existing = await FindExistingCoreAsync(
                appointment.RequestingAccountId,
                appointment.IdempotencyKey,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return new AppointmentRequestSaveResult(existing, NewlyCreated: false);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (transaction is null)
        {
            return;
        }

        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsync();
            transaction = null;
        }
    }

    private Task<AppointmentRequestState?> FindExistingCoreAsync(
        EntityId requestingAccountId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken) =>
        (from appointment in dbContext.Appointments.AsNoTracking()
         join slot in dbContext.AvailabilitySlots.AsNoTracking()
             on appointment.AvailabilitySlotId equals slot.Id
         where appointment.RequestingAccountId == requestingAccountId &&
               appointment.IdempotencyKey == idempotencyKey
         select new AppointmentRequestState(appointment, slot))
        .SingleOrDefaultAsync(cancellationToken);

    private async Task RollbackAndClearAsync(CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            transaction = null;
        }

        dbContext.ChangeTracker.Clear();
    }

    private void EnsureTransactionActive()
    {
        if (transaction is null)
        {
            throw new InvalidOperationException(
                "An appointment request transaction has not been started.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception, string constraint) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: var constraintName
        } && string.Equals(constraintName, constraint, StringComparison.Ordinal);
}
