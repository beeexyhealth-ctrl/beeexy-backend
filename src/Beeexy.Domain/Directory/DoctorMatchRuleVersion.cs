using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

public sealed class DoctorMatchRuleVersion
{
    private DoctorMatchRuleVersion()
    {
        Version = null!;
    }

    private DoctorMatchRuleVersion(EntityId id, DirectoryCode version, DateTimeOffset createdAt)
    {
        Id = id;
        Version = version;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public DirectoryCode Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static DoctorMatchRuleVersion Create(
        DirectoryCode version,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(version);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        var entityId = id ?? EntityId.New();
        DirectoryValueGuard.EnsureNonEmpty(entityId, nameof(id));
        return new DoctorMatchRuleVersion(entityId, version, createdAt);
    }
}
