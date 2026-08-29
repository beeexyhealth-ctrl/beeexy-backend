using Beeexy.Domain.Common;

namespace Beeexy.Application.Directory;

public interface IClinicDirectoryReadRepository
{
    Task<bool> CursorExistsAsync(
        ClinicDirectoryPageCursor cursor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClinicDirectoryListItem>> ListAsync(
        ClinicDirectoryFilter filter,
        ClinicDirectoryPageCursor? after,
        int take,
        CancellationToken cancellationToken = default);

    Task<ClinicDirectoryDetail?> GetAsync(
        EntityId clinicId,
        CancellationToken cancellationToken = default);
}

public sealed record ClinicDirectoryFilter(
    string? Code,
    string? Locality,
    string? AdministrativeArea,
    string? Country);

public sealed record ClinicDirectoryPageCursor(
    ClinicDirectoryFilter Filter,
    EntityId ClinicId);

public sealed record ClinicDirectoryListItem(
    EntityId ClinicId,
    string Code,
    string Name);

public sealed record ClinicDirectoryDetail(
    EntityId ClinicId,
    string Code,
    string Name,
    IReadOnlyList<ClinicDirectoryLocation> Locations);

public sealed record ClinicDirectoryLocation(
    EntityId LocationId,
    string Name,
    string Locality,
    string AdministrativeArea,
    string Country,
    string TimeZone);
