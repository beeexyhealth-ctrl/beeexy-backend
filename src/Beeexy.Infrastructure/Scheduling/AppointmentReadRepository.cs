using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Scheduling;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Scheduling;

internal sealed class AppointmentReadRepository(BeeexyDbContext dbContext)
    : IAppointmentReadRepository
{
    public Task<bool> CursorExistsAsync(
        EntityId accessiblePrimaryProfileId,
        AppointmentPageCursor cursor,
        CancellationToken cancellationToken = default) =>
        ApplyFilter(
                AuthorizedAppointments(accessiblePrimaryProfileId),
                cursor.Filter)
            .AnyAsync(appointment =>
                appointment.Id == cursor.AppointmentId &&
                appointment.ScheduledStartAt == cursor.ScheduledStartAt,
                cancellationToken);

    public async Task<IReadOnlyList<AppointmentSummary>> ListAsync(
        EntityId accessiblePrimaryProfileId,
        AppointmentListFilter filter,
        AppointmentPageCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        var appointments = ApplyFilter(
            AuthorizedAppointments(accessiblePrimaryProfileId),
            filter);
        if (after is not null)
        {
            appointments = appointments.Where(appointment => EF.Functions.GreaterThan(
                ValueTuple.Create(
                    appointment.ScheduledStartAt,
                    appointment.Id),
                ValueTuple.Create(
                    after.ScheduledStartAt,
                    after.AppointmentId)));
        }

        return await (
            from appointment in appointments
            join slot in dbContext.AvailabilitySlots.AsNoTracking()
                on appointment.AvailabilitySlotId equals slot.Id
            orderby appointment.ScheduledStartAt, appointment.Id
            select new AppointmentSummary(
                appointment.Id,
                appointment.PatientProfileId,
                appointment.AvailabilitySlotId,
                slot.DoctorId,
                slot.ClinicId,
                slot.ClinicLocationId,
                appointment.Status,
                appointment.Modality,
                slot.StartsAt,
                slot.EndsAt,
                slot.ClinicTimeZone.Value,
                appointment.CreatedAt))
            .Take(take)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<EntityId?> FindPatientProfileIdAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default)
    {
        var patientId = await dbContext.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.Id == appointmentId)
            .Select(appointment => (Guid?)appointment.PatientProfileId.Value)
            .SingleOrDefaultAsync(cancellationToken);
        return patientId.HasValue ? EntityId.From(patientId.Value) : null;
    }

    public async Task<AppointmentDetail?> GetAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default)
    {
        var state = await (
            from appointment in dbContext.Appointments.AsNoTracking()
            join slot in dbContext.AvailabilitySlots.AsNoTracking()
                on appointment.AvailabilitySlotId equals slot.Id
            where appointment.Id == appointmentId
            select new { Appointment = appointment, Slot = slot })
            .SingleOrDefaultAsync(cancellationToken);
        if (state is null)
        {
            return null;
        }

        var statusHistory = await dbContext.AppointmentStatusHistory
            .AsNoTracking()
            .Where(history => history.AppointmentId == appointmentId)
            .OrderBy(history => history.Sequence)
            .Select(history => new AppointmentStatusHistoryItem(
                history.Sequence,
                history.PreviousStatus,
                history.NewStatus,
                history.ActorType,
                history.Action,
                history.OccurredAt))
            .ToArrayAsync(cancellationToken);
        var rescheduleHistory = await dbContext.AppointmentRescheduleHistory
            .AsNoTracking()
            .Where(history => history.AppointmentId == appointmentId)
            .OrderBy(history => history.OccurredAt)
            .ThenBy(history => history.Id)
            .Select(history => new AppointmentRescheduleHistoryItem(
                history.PreviousSlotId,
                history.NewSlotId,
                history.OccurredAt))
            .ToArrayAsync(cancellationToken);
        var currentAppointment = state.Appointment;
        var currentSlot = state.Slot;
        return new AppointmentDetail(
            new AppointmentSummary(
                currentAppointment.Id,
                currentAppointment.PatientProfileId,
                currentAppointment.AvailabilitySlotId,
                currentSlot.DoctorId,
                currentSlot.ClinicId,
                currentSlot.ClinicLocationId,
                currentAppointment.Status,
                currentAppointment.Modality,
                currentSlot.StartsAt,
                currentSlot.EndsAt,
                currentSlot.ClinicTimeZone.Value,
                currentAppointment.CreatedAt),
            currentAppointment.Reason?.Value,
            statusHistory,
            rescheduleHistory);
    }

    private IQueryable<Appointment> AuthorizedAppointments(EntityId primaryProfileId) =>
        dbContext.Appointments
            .AsNoTracking()
            .Where(appointment =>
                appointment.PatientProfileId == primaryProfileId ||
                dbContext.CareRelationships.Any(relationship =>
                    relationship.ManagerProfileId == primaryProfileId &&
                    relationship.SubjectProfileId == appointment.PatientProfileId &&
                    relationship.Status == CareRelationshipStatus.Active));

    private static IQueryable<Appointment> ApplyFilter(
        IQueryable<Appointment> appointments,
        AppointmentListFilter filter)
    {
        if (filter.PatientProfileId is { } patientProfileId)
        {
            appointments = appointments.Where(appointment =>
                appointment.PatientProfileId == patientProfileId);
        }

        if (filter.Status is { } status)
        {
            appointments = appointments.Where(appointment => appointment.Status == status);
        }

        if (filter.From is { } from)
        {
            appointments = appointments.Where(appointment =>
                appointment.ScheduledStartAt >= from);
        }

        if (filter.To is { } to)
        {
            appointments = appointments.Where(appointment =>
                appointment.ScheduledStartAt < to);
        }

        return appointments;
    }
}
