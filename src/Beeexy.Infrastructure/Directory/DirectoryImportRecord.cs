using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;

namespace Beeexy.Infrastructure.DirectoryServices;

internal sealed class DirectoryImportRecord
{
    private DirectoryImportRecord()
    {
        PackageCode = null!;
        Version = null!;
        ContentHash = null!;
    }

    private DirectoryImportRecord(
        EntityId id,
        DirectoryCode packageCode,
        DirectoryCode version,
        string contentHash,
        DateTimeOffset importedAt)
    {
        Id = id;
        PackageCode = packageCode;
        Version = version;
        ContentHash = contentHash;
        ImportedAt = importedAt;
    }

    public EntityId Id { get; private set; }

    public DirectoryCode PackageCode { get; private set; }

    public DirectoryCode Version { get; private set; }

    public string ContentHash { get; private set; }

    public DateTimeOffset ImportedAt { get; private set; }

    public static DirectoryImportRecord Create(
        DirectoryImportPackage package,
        DateTimeOffset importedAt,
        EntityId id)
    {
        return new DirectoryImportRecord(
            id,
            package.PackageCode,
            package.Version,
            package.ContentHash,
            importedAt);
    }
}
