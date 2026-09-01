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

public sealed class ConfirmAppointmentForOperations(AppointmentTransitionEngine transition)
{
    public Task<AppointmentTransitionResult> ExecuteAsync(
        EntityId appointmentId,
        string operationalActor,
        CancellationToken cancellationToken = default) =>
        transition.ExecuteAsync(
            appointmentId,
            AppointmentStatus.Confirmed,
            AppointmentActor.BeeexyOperations(operationalActor),
            authorizeClinic: null,
            cancellationToken);
}

public sealed class RejectAppointmentForOperations(AppointmentTransitionEngine transition)
{
    public Task<AppointmentTransitionResult> ExecuteAsync(
        EntityId appointmentId,
        string operationalActor,
        CancellationToken cancellationToken = default) =>
        transition.ExecuteAsync(
            appointmentId,
            AppointmentStatus.Rejected,
            AppointmentActor.BeeexyOperations(operationalActor),
            authorizeClinic: null,
            cancellationToken);
}

public sealed class TransitionAppointment
{
    private readonly CurrentAccountProfileResolver currentAccountResolver;
    private readonly AppointmentSchedulerAssignments schedulerAssignments;
    private readonly AppointmentTransitionEngine transition;

    public TransitionAppointment(
        IClock clock,
        CurrentAccountProfileResolver currentAccountResolver,
        AppointmentSchedulerAssignments schedulerAssignments,
        IAppointmentTransitionTransaction transaction)
    {
        this.currentAccountResolver = currentAccountResolver;
        this.schedulerAssignments = schedulerAssignments;
        transition = new AppointmentTransitionEngine(clock, transaction);
    }

    public async Task<AppointmentTransitionResult> ExecuteAsync(
        EntityId appointmentId,
        AppointmentStatus targetStatus,
        CancellationToken cancellationToken = default)
    {
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var actor = AppointmentActor.AppointmentScheduler(current.Account.Id);
        return await transition.ExecuteAsync(
            appointmentId,
            targetStatus,
            actor,
            clinicId => EnsureAuthorized(current.Account.Id, clinicId),
            cancellationToken);
    }

    private void EnsureAuthorized(EntityId accountId, EntityId clinicId)
    {
        if (!schedulerAssignments.HasAppointmentSchedulerPermission(accountId, clinicId))
        {
            throw new AppointmentSchedulerForbiddenException();
        }
    }
}

public sealed class AppointmentTransitionEngine(
    IClock clock,
    IAppointmentTransitionTransaction transaction)
{
    public async Task<AppointmentTransitionResult> ExecuteAsync(
        EntityId appointmentId,
        AppointmentStatus targetStatus,
        AppointmentActor actor,
        Action<EntityId>? authorizeClinic,
        CancellationToken cancellationToken)
    {
        if (appointmentId.Value == Guid.Empty)
        {
            throw new AppointmentNotFoundException();
        }

        if (targetStatus is not (AppointmentStatus.Confirmed or AppointmentStatus.Rejected))
        {
            throw new ArgumentOutOfRangeException(nameof(targetStatus));
        }

        await transaction.BeginAsync(cancellationToken);
        var state = await transaction.LoadAsync(appointmentId, cancellationToken)
            ?? throw new AppointmentNotFoundException();
        authorizeClinic?.Invoke(state.Slot.ClinicId);

        if (state.Appointment.Status == targetStatus)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AppointmentTransitionResult(
                AppointmentTransitionProjection.ToSummary(state),
                NewlyApplied: false);
        }

        try
        {
            Apply(state.Appointment, targetStatus, actor, UtcNow());
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
            authorizeClinic?.Invoke(reloaded.Slot.ClinicId);
            if (reloaded.Appointment.Status != targetStatus)
            {
                throw new AppointmentTransitionConflictException();
            }

            return new AppointmentTransitionResult(
                AppointmentTransitionProjection.ToSummary(reloaded),
                NewlyApplied: false);
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
        AppointmentActor actor,
        DateTimeOffset occurredAt)
    {
        if (targetStatus == AppointmentStatus.Confirmed)
        {
            appointment.Confirm(actor, occurredAt);
        }
        else
        {
            appointment.Reject(actor, occurredAt);
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
