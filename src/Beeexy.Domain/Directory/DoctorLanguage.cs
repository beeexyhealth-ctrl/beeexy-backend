using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

public sealed class DoctorLanguage
{
    private DoctorLanguage()
    {
    }

    private DoctorLanguage(EntityId id, EntityId doctorId, EntityId languageId, DateTimeOffset createdAt)
    {
        Id = id;
        DoctorId = doctorId;
        LanguageId = languageId;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId DoctorId { get; private set; }

    public EntityId LanguageId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static DoctorLanguage Create(
        EntityId doctorId,
        EntityId languageId,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        DirectoryValueGuard.EnsureNonEmpty(doctorId, nameof(doctorId));
        DirectoryValueGuard.EnsureNonEmpty(languageId, nameof(languageId));
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        var entityId = id ?? EntityId.New();
        DirectoryValueGuard.EnsureNonEmpty(entityId, nameof(id));
        return new DoctorLanguage(entityId, doctorId, languageId, createdAt);
    }
}
