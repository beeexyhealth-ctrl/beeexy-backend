using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

public sealed class InsurancePlan
{
    private InsurancePlan()
    {
        Code = null!;
        Name = null!;
    }

    private InsurancePlan(EntityId id, DirectoryCode code, DirectoryName name, DateTimeOffset createdAt)
    {
        Id = id;
        Code = code;
        Name = name;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public DirectoryCode Code { get; private set; }

    public DirectoryName Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static InsurancePlan Create(
        DirectoryCode code,
        DirectoryName name,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        var entityId = id ?? EntityId.New();
        DirectoryValueGuard.EnsureNonEmpty(entityId, nameof(id));
        return new InsurancePlan(entityId, code, name, createdAt);
    }
}
