using System.Data;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Beeexy.Infrastructure.Scheduling;

internal sealed class AppointmentTransitionTransaction(BeeexyDbContext dbContext)
    : IAppointmentTransitionTransaction
{
    private const string HistorySequenceConstraint =
        "ux_appointment_status_history_appointment_sequence";

    private IDbContextTransaction? transaction;

    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException(
                "The appointment transition transaction is already active.");
        }

        transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
    }

    public Task<AppointmentTransitionState?> LoadAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionActive();
        return LoadCoreAsync(appointmentId, tracked: true, cancellationToken);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        EnsureTransactionActive();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackAndClearAsync(cancellationToken);
            throw new AppointmentTransitionConcurrencyException(exception);
        }
        catch (DbUpdateException exception) when (IsHistorySequenceRace(exception))
        {
            await RollbackAndClearAsync(cancellationToken);
            throw new AppointmentTransitionConcurrencyException(exception);
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

    public Task<AppointmentTransitionState?> ReloadAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException(
                "A failed transition must be rolled back before it is reloaded.");
        }

        return LoadCoreAsync(appointmentId, tracked: false, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsync();
            transaction = null;
        }
    }

    private async Task<AppointmentTransitionState?> LoadCoreAsync(
        EntityId appointmentId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        var appointments = dbContext.Appointments.AsQueryable();
        if (!tracked)
        {
            appointments = appointments.AsNoTracking();
        }

        var appointment = await appointments
            .SingleOrDefaultAsync(value => value.Id == appointmentId, cancellationToken);
        if (appointment is null)
        {
            return null;
        }

        var slot = await dbContext.AvailabilitySlots
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == appointment.AvailabilitySlotId,
                cancellationToken);
        return new AppointmentTransitionState(appointment, slot);
    }

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
                "An appointment transition transaction has not been started.");
        }
    }

    private static bool IsHistorySequenceRace(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: HistorySequenceConstraint
        };
}
