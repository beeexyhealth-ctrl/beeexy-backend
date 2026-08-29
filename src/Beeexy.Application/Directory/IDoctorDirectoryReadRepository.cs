using Beeexy.Domain.Common;

namespace Beeexy.Application.Directory;

public interface IDoctorDirectoryReadRepository
{
    Task<bool> CursorExistsAsync(
        DoctorDirectoryPageCursor cursor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DoctorDirectoryProfile>> SearchAsync(
        DoctorDirectoryFilter filter,
        DoctorDirectoryPageCursor? after,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EntityId>> ListFilteredDoctorIdsAsync(
        DoctorDirectoryFilter filter,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DoctorDirectoryProfile>> GetManyAsync(
        IReadOnlyList<EntityId> doctorIds,
        CancellationToken cancellationToken = default);

    Task<DoctorDirectoryProfile?> GetAsync(
        EntityId doctorId,
        CancellationToken cancellationToken = default);
}

public sealed record DoctorDirectoryFilter(
    string? SpecialtyCode,
    string? LanguageCode,
    string? Locality,
    string? AdministrativeArea,
    string? Country,
    string? InsurancePlanCode);

public sealed record DoctorDirectoryPageCursor(
    DoctorDirectoryFilter Filter,
    EntityId DoctorId);

public sealed record RankedDoctorDirectoryPageCursor(
    DoctorDirectoryFilter Filter,
    string RuleVersion,
    int MatchScore,
    EntityId DoctorId);

public sealed record DoctorDirectoryProfile(
    EntityId DoctorId,
    string Code,
    string DisplayName,
    IReadOnlyList<DoctorDirectoryCatalogValue> Specialties,
    IReadOnlyList<DoctorDirectoryCatalogValue> Languages,
    IReadOnlyList<DoctorDirectoryAffiliation> Affiliations,
    IReadOnlyList<DoctorDirectoryCatalogValue> StoredInsuranceParticipations,
    IReadOnlyList<DoctorDirectoryCredential> Credentials);

public sealed record DoctorDirectoryCatalogValue(string Code, string Name);

public sealed record DoctorDirectoryAffiliation(
    EntityId ClinicId,
    string ClinicCode,
    string ClinicName,
    DoctorDirectoryLocation? Location);

public sealed record DoctorDirectoryLocation(
    EntityId LocationId,
    string Name,
    string Locality,
    string AdministrativeArea,
    string Country,
    string TimeZone);

public sealed record DoctorDirectoryCredential(string Name);
