using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.DirectoryServices;

internal sealed class DoctorDirectoryReadRepository(PublicDirectoryQueryBoundary boundary)
    : IDoctorDirectoryReadRepository
{
    public Task<bool> CursorExistsAsync(
        DoctorDirectoryPageCursor cursor,
        CancellationToken cancellationToken = default) =>
        BuildFilteredQuery(cursor.Filter)
            .AnyAsync(doctor => doctor.Id == cursor.DoctorId, cancellationToken);

    public async Task<IReadOnlyList<DoctorDirectoryProfile>> SearchAsync(
        DoctorDirectoryFilter filter,
        DoctorDirectoryPageCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        var doctors = await BuildFilteredQuery(filter, after?.DoctorId)
            .OrderBy(doctor => doctor.Id)
            .Take(take)
            .Select(doctor => new DoctorRow(
                doctor.Id,
                doctor.Code.Value,
                doctor.DisplayName.Value))
            .ToArrayAsync(cancellationToken);

        return await LoadProfilesAsync(doctors, cancellationToken);
    }

    public async Task<IReadOnlyList<EntityId>> ListFilteredDoctorIdsAsync(
        DoctorDirectoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        return await BuildFilteredQuery(filter)
            .Select(doctor => doctor.Id)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorDirectoryProfile>> GetManyAsync(
        IReadOnlyList<EntityId> doctorIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(doctorIds);
        if (doctorIds.Count == 0)
        {
            return [];
        }

        var doctors = await boundary.Doctors()
            .Where(doctor => doctorIds.Contains(doctor.Id))
            .Select(doctor => new DoctorRow(
                doctor.Id,
                doctor.Code.Value,
                doctor.DisplayName.Value))
            .ToArrayAsync(cancellationToken);
        var byId = doctors.ToDictionary(doctor => doctor.DoctorId);
        if (byId.Count != doctorIds.Count)
        {
            throw new InvalidOperationException(
                "A ranked doctor page no longer satisfies the public visibility boundary.");
        }

        var ordered = doctorIds.Select(doctorId => byId[doctorId]).ToArray();
        return await LoadProfilesAsync(ordered, cancellationToken);
    }

    public async Task<DoctorDirectoryProfile?> GetAsync(
        EntityId doctorId,
        CancellationToken cancellationToken = default)
    {
        var doctor = await boundary.Doctors()
            .Where(value => value.Id == doctorId)
            .Select(value => new DoctorRow(
                value.Id,
                value.Code.Value,
                value.DisplayName.Value))
            .SingleOrDefaultAsync(cancellationToken);
        if (doctor is null)
        {
            return null;
        }

        return (await LoadProfilesAsync([doctor], cancellationToken))[0];
    }

    private IQueryable<Doctor> BuildFilteredQuery(
        DoctorDirectoryFilter filter,
        EntityId? after = null)
    {
        var doctors = after.HasValue
            ? boundary.DoctorsAfter(after.Value)
            : boundary.Doctors();

        if (filter.SpecialtyCode is not null)
        {
            var specialtyCode = DirectoryCode.Create(filter.SpecialtyCode);
            var relationships = boundary.DoctorSpecialties().Join(
                boundary.Specialties().Where(value => value.Code == specialtyCode),
                relationship => relationship.SpecialtyId,
                specialty => specialty.Id,
                (relationship, _) => relationship);
            doctors = doctors.Where(doctor =>
                relationships.Any(value => value.DoctorId == doctor.Id));
        }

        if (filter.LanguageCode is not null)
        {
            var languageCode = DirectoryCode.Create(filter.LanguageCode);
            var relationships = boundary.DoctorLanguages().Join(
                boundary.Languages().Where(value => value.Code == languageCode),
                relationship => relationship.LanguageId,
                language => language.Id,
                (relationship, _) => relationship);
            doctors = doctors.Where(doctor =>
                relationships.Any(value => value.DoctorId == doctor.Id));
        }

        if (filter.InsurancePlanCode is not null)
        {
            var insurancePlanCode = DirectoryCode.Create(filter.InsurancePlanCode);
            var relationships = boundary.DoctorInsuranceParticipations().Join(
                boundary.InsurancePlans().Where(value => value.Code == insurancePlanCode),
                relationship => relationship.InsurancePlanId,
                plan => plan.Id,
                (relationship, _) => relationship);
            doctors = doctors.Where(doctor =>
                relationships.Any(value => value.DoctorId == doctor.Id));
        }

        if (filter.Locality is null &&
            filter.AdministrativeArea is null &&
            filter.Country is null)
        {
            return doctors;
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

        var affiliations = boundary.DoctorAffiliations()
            .Where(affiliation => affiliation.ClinicLocationId.HasValue);
        return doctors.Where(doctor => affiliations.Any(affiliation =>
            affiliation.DoctorId == doctor.Id &&
            locations.Any(location =>
                location.Id == affiliation.ClinicLocationId!.Value)));
    }

    private async Task<IReadOnlyList<DoctorDirectoryProfile>> LoadProfilesAsync(
        IReadOnlyList<DoctorRow> doctors,
        CancellationToken cancellationToken)
    {
        if (doctors.Count == 0)
        {
            return [];
        }

        var doctorIds = doctors.Select(doctor => doctor.DoctorId).ToArray();
        var specialties = await boundary.DoctorSpecialties()
            .Where(relationship => doctorIds.Contains(relationship.DoctorId))
            .Join(
                boundary.Specialties(),
                relationship => relationship.SpecialtyId,
                specialty => specialty.Id,
                (relationship, specialty) => new
                {
                    relationship.DoctorId,
                    Code = specialty.Code.Value,
                    Name = specialty.Name.Value
                })
            .Select(value => new CatalogRow(value.DoctorId, value.Code, value.Name))
            .ToArrayAsync(cancellationToken);

        var languages = await boundary.DoctorLanguages()
            .Where(relationship => doctorIds.Contains(relationship.DoctorId))
            .Join(
                boundary.Languages(),
                relationship => relationship.LanguageId,
                language => language.Id,
                (relationship, language) => new
                {
                    relationship.DoctorId,
                    Code = language.Code.Value,
                    Name = language.Name.Value
                })
            .Select(value => new CatalogRow(value.DoctorId, value.Code, value.Name))
            .ToArrayAsync(cancellationToken);

        var insurance = await boundary.DoctorInsuranceParticipations()
            .Where(relationship => doctorIds.Contains(relationship.DoctorId))
            .Join(
                boundary.InsurancePlans(),
                relationship => relationship.InsurancePlanId,
                plan => plan.Id,
                (relationship, plan) => new
                {
                    relationship.DoctorId,
                    Code = plan.Code.Value,
                    Name = plan.Name.Value
                })
            .Select(value => new CatalogRow(value.DoctorId, value.Code, value.Name))
            .ToArrayAsync(cancellationToken);

        var affiliations = await (
            from affiliation in boundary.DoctorAffiliations()
            where doctorIds.Contains(affiliation.DoctorId)
            join clinic in boundary.Clinics()
                on affiliation.ClinicId equals clinic.Id
            join location in boundary.ClinicLocations()
                on affiliation.ClinicLocationId equals (EntityId?)location.Id into locations
            from location in locations.DefaultIfEmpty()
            orderby affiliation.DoctorId, affiliation.Id
            select new AffiliationRow(
                affiliation.DoctorId,
                clinic.Id,
                clinic.Code.Value,
                clinic.Name.Value,
                affiliation.ClinicLocationId,
                location == null ? null : location.Name.Value,
                location == null ? null : location.Locality,
                location == null ? null : location.AdministrativeArea,
                location == null ? null : location.Country,
                location == null ? null : location.TimeZone.Value))
            .ToArrayAsync(cancellationToken);

        var credentials = await boundary.DoctorCredentials()
            .Where(credential => doctorIds.Contains(credential.DoctorId))
            .OrderBy(credential => credential.DoctorId)
            .ThenBy(credential => credential.Id)
            .Select(credential => new CredentialRow(
                credential.DoctorId,
                credential.Name.Value))
            .ToArrayAsync(cancellationToken);

        var specialtiesByDoctor = specialties.ToLookup(value => value.DoctorId);
        var languagesByDoctor = languages.ToLookup(value => value.DoctorId);
        var insuranceByDoctor = insurance.ToLookup(value => value.DoctorId);
        var affiliationsByDoctor = affiliations.ToLookup(value => value.DoctorId);
        var credentialsByDoctor = credentials.ToLookup(value => value.DoctorId);

        return doctors.Select(doctor => new DoctorDirectoryProfile(
            doctor.DoctorId,
            doctor.Code,
            doctor.DisplayName,
            specialtiesByDoctor[doctor.DoctorId]
                .OrderBy(value => value.Code, StringComparer.Ordinal)
                .Select(value => new DoctorDirectoryCatalogValue(value.Code, value.Name))
                .ToArray(),
            languagesByDoctor[doctor.DoctorId]
                .OrderBy(value => value.Code, StringComparer.Ordinal)
                .Select(value => new DoctorDirectoryCatalogValue(value.Code, value.Name))
                .ToArray(),
            affiliationsByDoctor[doctor.DoctorId]
                .Select(ToAffiliation)
                .ToArray(),
            insuranceByDoctor[doctor.DoctorId]
                .OrderBy(value => value.Code, StringComparer.Ordinal)
                .Select(value => new DoctorDirectoryCatalogValue(value.Code, value.Name))
                .ToArray(),
            credentialsByDoctor[doctor.DoctorId]
                .Select(value => new DoctorDirectoryCredential(value.Name))
                .ToArray()))
            .ToArray();
    }

    private static DoctorDirectoryAffiliation ToAffiliation(AffiliationRow value) =>
        new(
            value.ClinicId,
            value.ClinicCode,
            value.ClinicName,
            value.LocationId.HasValue
                ? new DoctorDirectoryLocation(
                    value.LocationId.Value,
                    value.LocationName!,
                    value.Locality!,
                    value.AdministrativeArea!,
                    value.Country!,
                    value.TimeZone!)
                : null);

    private sealed record DoctorRow(EntityId DoctorId, string Code, string DisplayName);

    private sealed record CatalogRow(EntityId DoctorId, string Code, string Name);

    private sealed record CredentialRow(EntityId DoctorId, string Name);

    private sealed record AffiliationRow(
        EntityId DoctorId,
        EntityId ClinicId,
        string ClinicCode,
        string ClinicName,
        EntityId? LocationId,
        string? LocationName,
        string? Locality,
        string? AdministrativeArea,
        string? Country,
        string? TimeZone);
}
