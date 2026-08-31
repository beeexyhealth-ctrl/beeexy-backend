using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Application.Scheduling;

public sealed record AppointmentRescheduleTargetState(
    AvailabilitySlot Slot,
    bool HasEligibleDirectoryRelationships,
    bool IsReserved);

public interface IAppointmentRescheduleTransaction : IAsyncDisposable
{
    Task BeginAsync(CancellationToken cancellationToken = default);

    Task<AppointmentTransitionState?> LoadAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default);

    Task<AppointmentRescheduleTargetState?> FindTargetSlotAsync(
        EntityId slotId,
        CancellationToken cancellationToken = default);

    void Add(AppointmentRescheduleHistory history);

    Task SaveAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task<AppointmentTransitionState?> ReloadAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default);
}

public sealed class AppointmentRescheduleConflictException : Exception
{
    public AppointmentRescheduleConflictException()
        : base("The appointment cannot apply the requested reschedule operation.")
    {
    }
}

public sealed class RescheduleAppointment(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    AuthorizePatientAccess authorizePatientAccess,
    IAppointmentRescheduleTransaction transaction)
{
    public async Task<AppointmentTransitionResult> ExecuteAsync(
        EntityId appointmentId,
        EntityId targetSlotId,
        CancellationToken cancellationToken = default)
    {
        if (appointmentId.Value == Guid.Empty || targetSlotId.Value == Guid.Empty)
        {
            throw new AppointmentNotFoundException();
        }

        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        await transaction.BeginAsync(cancellationToken);
        var state = await transaction.LoadAsync(appointmentId, cancellationToken)
            ?? throw new AppointmentNotFoundException();
        await EnsureAuthorizedForMutationAsync(
            state.Appointment.PatientProfileId,
            current,
            cancellationToken);
        EnsureSourceStatus(state.Appointment.Status);

        var target = await transaction.FindTargetSlotAsync(
            targetSlotId,
            cancellationToken);
        if (target is null || !target.HasEligibleDirectoryRelationships)
        {
            throw new AppointmentNotFoundException();
        }

        if (target.Slot.Id == state.Appointment.AvailabilitySlotId)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AppointmentTransitionResult(
                AppointmentTransitionProjection.ToSummary(state),
                NewlyApplied: false);
        }

        ValidateTarget(target, state.Appointment.Modality, UtcNow());
        var occurredAt = UtcNow();
        var history = state.Appointment.Reschedule(
            target.Slot,
            current.Account.Id,
            occurredAt) ?? throw new InvalidOperationException(
                "A distinct target slot must create a reschedule audit record.");
        transaction.Add(history);
        var updatedState = new AppointmentTransitionState(
            state.Appointment,
            target.Slot);

        try
        {
            await transaction.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AppointmentTransitionResult(
                AppointmentTransitionProjection.ToSummary(updatedState),
                NewlyApplied: true);
        }
        catch (AppointmentTransitionConcurrencyException)
        {
            var reloaded = await transaction.ReloadAsync(appointmentId, cancellationToken)
                ?? throw new AppointmentRescheduleConflictException();
            await EnsureAuthorizedForReadAsync(
                reloaded.Appointment.PatientProfileId,
                current,
                cancellationToken);
            if (reloaded.Appointment.AvailabilitySlotId != targetSlotId ||
                reloaded.Appointment.Status != state.Appointment.Status)
            {
                throw new AppointmentRescheduleConflictException();
            }

            return new AppointmentTransitionResult(
                AppointmentTransitionProjection.ToSummary(reloaded),
                NewlyApplied: false);
        }
    }

    private async Task EnsureAuthorizedForMutationAsync(
        EntityId patientProfileId,
        ResolvedCurrentAccountProfile current,
        CancellationToken cancellationToken)
    {
        var authorization = await authorizePatientAccess.ExecuteForPatientUpdateAsync(
            patientProfileId,
            current,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new AppointmentNotFoundException();
        }
    }

    private async Task EnsureAuthorizedForReadAsync(
        EntityId patientProfileId,
        ResolvedCurrentAccountProfile current,
        CancellationToken cancellationToken)
    {
        var authorization = await authorizePatientAccess.ExecuteAsync(
            patientProfileId,
            current,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new AppointmentNotFoundException();
        }
    }

    private static void EnsureSourceStatus(AppointmentStatus status)
    {
        if (status is not (AppointmentStatus.Requested or AppointmentStatus.Confirmed))
        {
            throw new AppointmentRescheduleConflictException();
        }
    }

    private static void ValidateTarget(
        AppointmentRescheduleTargetState target,
        AppointmentModality appointmentModality,
        DateTimeOffset now)
    {
        if (!target.Slot.IsPublished)
        {
            throw new RequestValidationException(
                "scheduling.slot_unbookable",
                "The target availability slot cannot be requested.");
        }

        if (target.Slot.StartsAt <= now)
        {
            throw new RequestValidationException(
                "scheduling.slot_expired",
                "The target availability slot is no longer in the future.");
        }

        if (target.Slot.Modality != appointmentModality)
        {
            throw new RequestValidationException(
                "scheduling.modality_mismatch",
                "The appointment modality does not match the target slot.");
        }

        if (target.IsReserved)
        {
            throw new AppointmentSlotReservationConflictException();
        }
    }

    private DateTimeOffset UtcNow()
    {
        var utc = clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}
