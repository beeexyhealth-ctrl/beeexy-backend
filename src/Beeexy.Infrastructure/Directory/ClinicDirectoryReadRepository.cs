using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.DirectoryServices;

internal sealed class ClinicDirectoryReadRepository(PublicDirectoryQueryBoundary boundary)
    : IClinicDirectoryReadRepository
{
    public Task<bool> CursorExistsAsync(
        ClinicDirectoryPageCursor cursor,
        CancellationToken cancellationToken = default) =>
        BuildFilteredQuery(cursor.Filter)
            .AnyAsync(clinic => clinic.Id == cursor.ClinicId, cancellationToken);

    public async Task<IReadOnlyList<ClinicDirectoryListItem>> ListAsync(
        ClinicDirectoryFilter filter,
        ClinicDirectoryPageCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        var query = BuildFilteredQuery(filter, after?.ClinicId);

        return await query
            .OrderBy(clinic => clinic.Id)
            .Take(take)
            .Select(clinic => new ClinicDirectoryListItem(
                clinic.Id,
                clinic.Code.Value,
                clinic.Name.Value))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<ClinicDirectoryDetail?> GetAsync(
        EntityId clinicId,
        CancellationToken cancellationToken = default)
    {
        var clinic = await boundary.Clinics()
            .Where(value => value.Id == clinicId)
            .Select(value => new ClinicDirectoryListItem(
                value.Id,
                value.Code.Value,
                value.Name.Value))
            .SingleOrDefaultAsync(cancellationToken);
        if (clinic is null)
        {
            return null;
        }

        var locations = await boundary.ClinicLocations()
            .Where(location => location.ClinicId == clinicId)
            .OrderBy(location => location.Id)
            .Select(location => new ClinicDirectoryLocation(
                location.Id,
                location.Name.Value,
                location.Locality,
                location.AdministrativeArea,
                location.Country,
                location.TimeZone.Value))
            .ToArrayAsync(cancellationToken);

        return new ClinicDirectoryDetail(
            clinic.ClinicId,
            clinic.Code,
            clinic.Name,
            locations);
    }

    private IQueryable<Clinic> BuildFilteredQuery(
        ClinicDirectoryFilter filter,
        EntityId? after = null)
    {
        var clinics = after.HasValue
            ? boundary.ClinicsAfter(after.Value)
            : boundary.Clinics();
        if (filter.Code is not null)
        {
            var code = DirectoryCode.Create(filter.Code);
            clinics = clinics.Where(clinic => clinic.Code == code);
        }

        if (filter.Locality is null &&
            filter.AdministrativeArea is null &&
            filter.Country is null)
        {
            return clinics;
        }

        var locations = boundary.ClinicLocations();
        if (filter.Locality is not null)
        {
            locations = locations.Where(location => location.Locality == filter.Locality);
        }

        if (filter.AdministrativeArea is not null)
        {
            locations = locations.Where(location =>
                location.AdministrativeArea == filter.AdministrativeArea);
        }

        if (filter.Country is not null)
        {
            locations = locations.Where(location => location.Country == filter.Country);
        }

        return clinics.Where(clinic =>
            locations.Any(location => location.ClinicId == clinic.Id));
    }
}
