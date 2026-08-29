using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

public sealed class Clinic
{
    private Clinic()
    {
        Code = null!;
        Name = null!;
    }

    private Clinic(
        EntityId id,
        DirectoryCode code,
        DirectoryName name,
        bool isPublished,
        DateTimeOffset createdAt)
    {
        Id = id;
        Code = code;
        Name = name;
        IsPublished = isPublished;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public DirectoryCode Code { get; private set; }

    public DirectoryName Name { get; private set; }

    public bool IsPublished { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static Clinic Create(
        DirectoryCode code,
        DirectoryName name,
        bool isPublished,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        var entityId = id ?? EntityId.New();
        DirectoryValueGuard.EnsureNonEmpty(entityId, nameof(id));
        return new Clinic(entityId, code, name, isPublished, createdAt);
    }
}
