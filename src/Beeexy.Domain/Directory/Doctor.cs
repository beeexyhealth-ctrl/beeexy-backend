using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

public sealed class Doctor
{
    private Doctor()
    {
        Code = null!;
        DisplayName = null!;
    }

    private Doctor(
        EntityId id,
        DirectoryCode code,
        DirectoryName displayName,
        bool isPublished,
        DateTimeOffset createdAt)
    {
        Id = id;
        Code = code;
        DisplayName = displayName;
        IsPublished = isPublished;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public DirectoryCode Code { get; private set; }

    public DirectoryName DisplayName { get; private set; }

    public bool IsPublished { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static Doctor Create(
        DirectoryCode code,
        DirectoryName displayName,
        bool isPublished,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(displayName);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        var entityId = id ?? EntityId.New();
        DirectoryValueGuard.EnsureNonEmpty(entityId, nameof(id));
        return new Doctor(entityId, code, displayName, isPublished, createdAt);
    }
}
