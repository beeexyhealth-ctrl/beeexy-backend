using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;

namespace Beeexy.Infrastructure.Scheduling;

internal sealed class AvailabilityImportRecord
{
    private AvailabilityImportRecord()
    {
        PackageCode = null!;
        Version = null!;
        ContentHash = null!;
    }

    private AvailabilityImportRecord(
        EntityId id,
        DirectoryCode packageCode,
        DirectoryCode version,
        DateOnly referenceDate,
        string contentHash,
        DateTimeOffset importedAt)
    {
        Id = id;
        PackageCode = packageCode;
        Version = version;
        ReferenceDate = referenceDate;
        ContentHash = contentHash;
        ImportedAt = importedAt;
    }

    public EntityId Id { get; private set; }

    public DirectoryCode PackageCode { get; private set; }

    public DirectoryCode Version { get; private set; }

    public DateOnly ReferenceDate { get; private set; }

    public string ContentHash { get; private set; }

    public DateTimeOffset ImportedAt { get; private set; }

    public static AvailabilityImportRecord Create(
        AvailabilityImportPackage package,
        DateTimeOffset importedAt,
        EntityId id) =>
        new(
            id,
            package.PackageCode,
            package.Version,
            package.ReferenceDate,
            package.ContentHash,
            importedAt);
}
