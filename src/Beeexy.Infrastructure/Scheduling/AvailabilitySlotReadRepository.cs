using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;
using Beeexy.Infrastructure.DirectoryServices;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Scheduling;

internal sealed class AvailabilitySlotReadRepository(
    BeeexyDbContext dbContext,
    PublicDirectoryQueryBoundary publicDirectory) : IAvailabilitySlotReadRepository
{
    public async Task<IReadOnlyList<AvailableSlot>> ListAvailableAsync(
        EntityId doctorId,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset futureCutoff,
        CancellationToken cancellationToken = default)
    {
        return await BuildQuery(doctorId, from, to, futureCutoff)
            .ToArrayAsync(cancellationToken);
    }

    internal IQueryable<AvailableSlot> BuildQuery(
        EntityId doctorId,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset futureCutoff)
    {
        var publicClinics = publicDirectory.Clinics();
        var publicLocations = publicDirectory.ClinicLocations();
        var publicAffiliations = publicDirectory.DoctorAffiliations();

        return dbContext.AvailabilitySlots
            .AsNoTracking()
            .Where(slot =>
                slot.DoctorId == doctorId &&
                slot.IsPublished &&
                slot.StartsAt >= from &&
                slot.StartsAt > futureCutoff &&
                slot.StartsAt < to &&
                publicClinics.Any(clinic => clinic.Id == slot.ClinicId) &&
                publicLocations.Any(location =>
                    location.Id == slot.ClinicLocationId &&
                    location.ClinicId == slot.ClinicId) &&
                publicAffiliations.Any(affiliation =>
                    affiliation.DoctorId == slot.DoctorId &&
                    affiliation.ClinicId == slot.ClinicId &&
                    affiliation.ClinicLocationId == slot.ClinicLocationId) &&
                !dbContext.Appointments.Any(appointment =>
                    appointment.AvailabilitySlotId == slot.Id &&
                    (appointment.Status == AppointmentStatus.Requested ||
                     appointment.Status == AppointmentStatus.Confirmed)))
            .OrderBy(slot => slot.StartsAt)
            .ThenBy(slot => slot.Id)
            .Select(slot => new AvailableSlot(
                slot.Id,
                slot.DoctorId,
                slot.ClinicId,
                slot.ClinicLocationId,
                slot.StartsAt,
                slot.EndsAt,
                slot.ClinicTimeZone.Value,
                slot.Modality));
    }
}
