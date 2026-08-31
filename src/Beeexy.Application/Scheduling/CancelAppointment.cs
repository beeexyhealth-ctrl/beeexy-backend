using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Application.Scheduling;

public sealed class CancelAppointment(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    AuthorizePatientAccess authorizePatientAccess,
    IAppointmentTransitionTransaction transaction)
{
    public async Task<AppointmentTransitionResult> ExecuteAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default)
    {
        if (appointmentId.Value == Guid.Empty)
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

        if (state.Appointment.Status == AppointmentStatus.Cancelled)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AppointmentTransitionResult(
                AppointmentTransitionProjection.ToSummary(state),
                NewlyApplied: false);
        }

        try
        {
            state.Appointment.Cancel(current.Account.Id, UtcNow());
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
            await EnsureAuthorizedForReadAsync(
                reloaded.Appointment.PatientProfileId,
                current,
                cancellationToken);
            if (reloaded.Appointment.Status != AppointmentStatus.Cancelled)
            {
                throw new AppointmentTransitionConflictException();
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

    private DateTimeOffset UtcNow()
    {
        var utc = clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}
