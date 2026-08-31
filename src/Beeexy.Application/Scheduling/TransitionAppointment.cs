using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Application.Scheduling;

public sealed record AppointmentTransitionState(
    Appointment Appointment,
    AvailabilitySlot Slot);

public sealed record AppointmentTransitionResult(
    AppointmentSummary Appointment,
    bool NewlyApplied);

public interface IAppointmentTransitionTransaction : IAsyncDisposable
{
    Task BeginAsync(CancellationToken cancellationToken = default);

    Task<AppointmentTransitionState?> LoadAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task<AppointmentTransitionState?> ReloadAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default);
}

public sealed class AppointmentSchedulerForbiddenException : Exception
{
    public AppointmentSchedulerForbiddenException()
        : base("The authenticated account cannot schedule appointments for this clinic.")
    {
    }
}

public sealed class AppointmentTransitionConflictException : Exception
{
    public AppointmentTransitionConflictException()
        : base("The appointment cannot apply the requested status transition.")
    {
    }
}

public sealed class AppointmentTransitionConcurrencyException : Exception
{
    public AppointmentTransitionConcurrencyException(Exception innerException)
        : base("The appointment changed concurrently.", innerException)
    {
    }
}

public sealed class ConfirmAppointment(TransitionAppointment transition)
{
    public Task<AppointmentTransitionResult> ExecuteAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default) =>
        transition.ExecuteAsync(
            appointmentId,
            AppointmentStatus.Confirmed,
            cancellationToken);
}

public sealed class RejectAppointment(TransitionAppointment transition)
{
    public Task<AppointmentTransitionResult> ExecuteAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default) =>
        transition.ExecuteAsync(
            appointmentId,
            AppointmentStatus.Rejected,
            cancellationToken);
}

public sealed class TransitionAppointment(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    AppointmentSchedulerAssignments schedulerAssignments,
    IAppointmentTransitionTransaction transaction)
{
    public async Task<AppointmentTransitionResult> ExecuteAsync(
        EntityId appointmentId,
        AppointmentStatus targetStatus,
        CancellationToken cancellationToken = default)
    {
        if (appointmentId.Value == Guid.Empty)
        {
            throw new AppointmentNotFoundException();
        }

        if (targetStatus is not (AppointmentStatus.Confirmed or AppointmentStatus.Rejected))
        {
            throw new ArgumentOutOfRangeException(nameof(targetStatus));
        }

        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        await transaction.BeginAsync(cancellationToken);
        var state = await transaction.LoadAsync(appointmentId, cancellationToken)
            ?? throw new AppointmentNotFoundException();
        EnsureAuthorized(current.Account.Id, state.Slot.ClinicId);

        if (state.Appointment.Status == targetStatus)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AppointmentTransitionResult(
                AppointmentTransitionProjection.ToSummary(state),
                NewlyApplied: false);
        }

        try
        {
            Apply(state.Appointment, targetStatus, current.Account.Id, UtcNow());
        }
        catch (InvalidOperationException)
        {
            throw new AppointmentTransitionConflictException();
        }

        try
        {
            await transaction.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AppointmentTransitionResult(
                AppointmentTransitionProjection.ToSummary(state),
                NewlyApplied: true);
        }
        catch (AppointmentTransitionConcurrencyException)
        {
            var reloaded = await transaction.ReloadAsync(appointmentId, cancellationToken)
                ?? throw new AppointmentTransitionConflictException();
            EnsureAuthorized(current.Account.Id, reloaded.Slot.ClinicId);
            if (reloaded.Appointment.Status != targetStatus)
            {
                throw new AppointmentTransitionConflictException();
            }

            return new AppointmentTransitionResult(
                AppointmentTransitionProjection.ToSummary(reloaded),
                NewlyApplied: false);
        }
    }

    private void EnsureAuthorized(EntityId accountId, EntityId clinicId)
    {
        if (!schedulerAssignments.HasAppointmentSchedulerPermission(accountId, clinicId))
        {
            throw new AppointmentSchedulerForbiddenException();
        }
    }

    private DateTimeOffset UtcNow()
    {
        var utc = clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }

    private static void Apply(
        Appointment appointment,
        AppointmentStatus targetStatus,
        EntityId actorAccountId,
        DateTimeOffset occurredAt)
    {
        if (targetStatus == AppointmentStatus.Confirmed)
        {
            appointment.Confirm(actorAccountId, occurredAt);
        }
        else
        {
            appointment.Reject(actorAccountId, occurredAt);
        }
    }

}

internal static class AppointmentTransitionProjection
{
    public static AppointmentSummary ToSummary(AppointmentTransitionState state) => new(
        state.Appointment.Id,
        state.Appointment.PatientProfileId,
        state.Appointment.AvailabilitySlotId,
        state.Slot.DoctorId,
        state.Slot.ClinicId,
        state.Slot.ClinicLocationId,
        state.Appointment.Status,
        state.Appointment.Modality,
        state.Slot.StartsAt,
        state.Slot.EndsAt,
        state.Slot.ClinicTimeZone.Value,
        state.Appointment.CreatedAt);
}
