using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

public sealed class Language
{
    private Language()
    {
        Code = null!;
        Name = null!;
    }

    private Language(EntityId id, DirectoryCode code, DirectoryName name, DateTimeOffset createdAt)
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

    public static Language Create(
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
        return new Language(entityId, code, name, createdAt);
    }
}
