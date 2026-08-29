using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.DirectoryServices;

internal sealed class DoctorMatchingRepository(
    BeeexyDbContext dbContext,
    PublicDirectoryQueryBoundary boundary) : IDoctorMatchingRepository
{
    public Task<DoctorMatchRuleDefinition?> GetRuleAsync(
        DirectoryCode version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        return (
            from ruleVersion in dbContext.DoctorMatchRuleVersions.AsNoTracking()
            join configuration in dbContext.DoctorMatchRuleConfigurations.AsNoTracking()
                on ruleVersion.Id equals configuration.RuleVersionId
            where ruleVersion.Version == version
            select new DoctorMatchRuleDefinition(
                configuration.PackageCode.Value,
                ruleVersion.Version.Value,
                configuration.ContentHash,
                configuration.SpecialtyWeightPoints,
                configuration.LanguageWeightPoints,
                configuration.LocationWeightPoints,
                configuration.StoredInsuranceWeightPoints))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorMatchCandidateSnapshot>> ListEligibleCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        var doctorIds = await boundary.Doctors()
            .Select(doctor => doctor.Id)
            .ToArrayAsync(cancellationToken);
        if (doctorIds.Length == 0)
        {
            return [];
        }

        var specialties = await boundary.DoctorSpecialties()
            .Where(relationship => doctorIds.Contains(relationship.DoctorId))
            .Join(
                boundary.Specialties(),
                relationship => relationship.SpecialtyId,
                specialty => specialty.Id,
                (relationship, specialty) => new CandidateCode(
                    relationship.DoctorId,
                    specialty.Code.Value))
            .ToArrayAsync(cancellationToken);
        var languages = await boundary.DoctorLanguages()
            .Where(relationship => doctorIds.Contains(relationship.DoctorId))
            .Join(
                boundary.Languages(),
                relationship => relationship.LanguageId,
                language => language.Id,
                (relationship, language) => new CandidateCode(
                    relationship.DoctorId,
                    language.Code.Value))
            .ToArrayAsync(cancellationToken);
        var insurance = await boundary.DoctorInsuranceParticipations()
            .Where(relationship => doctorIds.Contains(relationship.DoctorId))
            .Join(
                boundary.InsurancePlans(),
                relationship => relationship.InsurancePlanId,
                plan => plan.Id,
                (relationship, plan) => new CandidateCode(
                    relationship.DoctorId,
                    plan.Code.Value))
            .ToArrayAsync(cancellationToken);
        var locations = await (
            from affiliation in boundary.DoctorAffiliations()
            where doctorIds.Contains(affiliation.DoctorId) &&
                affiliation.ClinicLocationId.HasValue
            join location in boundary.ClinicLocations()
                on affiliation.ClinicLocationId!.Value equals location.Id
            select new CandidateLocation(
                affiliation.DoctorId,
                location.Locality,
                location.AdministrativeArea,
                location.Country))
            .ToArrayAsync(cancellationToken);

        var specialtiesByDoctor = specialties.ToLookup(value => value.DoctorId);
        var languagesByDoctor = languages.ToLookup(value => value.DoctorId);
        var insuranceByDoctor = insurance.ToLookup(value => value.DoctorId);
        var locationsByDoctor = locations.ToLookup(value => value.DoctorId);
        return doctorIds.Select(doctorId => new DoctorMatchCandidateSnapshot(
                doctorId,
                specialtiesByDoctor[doctorId]
                    .Select(value => value.Code)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                languagesByDoctor[doctorId]
                    .Select(value => value.Code)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                locationsByDoctor[doctorId]
                    .OrderBy(value => value.Country, StringComparer.Ordinal)
                    .ThenBy(value => value.AdministrativeArea, StringComparer.Ordinal)
                    .ThenBy(value => value.Locality, StringComparer.Ordinal)
                    .Select(value => new DoctorMatchCandidateLocation(
                        value.Locality,
                        value.AdministrativeArea,
                        value.Country))
                    .ToArray(),
                insuranceByDoctor[doctorId]
                    .Select(value => value.Code)
                    .Order(StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }

    private sealed record CandidateCode(EntityId DoctorId, string Code);

    private sealed record CandidateLocation(
        EntityId DoctorId,
        string Locality,
        string AdministrativeArea,
        string Country);
}
