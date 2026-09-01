using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Scheduling;

internal sealed class AppointmentOperationsReadRepository(BeeexyDbContext dbContext)
    : IAppointmentOperationsReadRepository
{
    public async Task<IReadOnlyList<OperationalAppointmentSummary>> ListRequestedAsync(
        EntityId clinicId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await (
            from appointment in dbContext.Appointments.AsNoTracking()
            join slot in dbContext.AvailabilitySlots.AsNoTracking()
                on appointment.AvailabilitySlotId equals slot.Id
            join doctor in dbContext.Doctors.AsNoTracking()
                on slot.DoctorId equals doctor.Id
            where appointment.Status == AppointmentStatus.Requested &&
                slot.ClinicId == clinicId
            orderby slot.StartsAt, appointment.Id
            select new OperationalAppointmentSummary(
                appointment.Id,
                slot.ClinicId,
                doctor.DisplayName.Value,
                slot.StartsAt,
                slot.EndsAt,
                slot.ClinicTimeZone.Value,
                appointment.Modality,
                appointment.Status,
                appointment.CreatedAt))
            .Take(limit)
            .ToArrayAsync(cancellationToken);

    public Task<OperationalAppointmentSummary?> GetAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default) => (
            from appointment in dbContext.Appointments.AsNoTracking()
            join slot in dbContext.AvailabilitySlots.AsNoTracking()
                on appointment.AvailabilitySlotId equals slot.Id
            join doctor in dbContext.Doctors.AsNoTracking()
                on slot.DoctorId equals doctor.Id
            where appointment.Id == appointmentId
            select new OperationalAppointmentSummary(
                appointment.Id,
                slot.ClinicId,
                doctor.DisplayName.Value,
                slot.StartsAt,
                slot.EndsAt,
                slot.ClinicTimeZone.Value,
                appointment.Modality,
                appointment.Status,
                appointment.CreatedAt))
        .SingleOrDefaultAsync(cancellationToken);
}
