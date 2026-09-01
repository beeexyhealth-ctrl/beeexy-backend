using Beeexy.Domain.Common;

namespace Beeexy.Domain.Ai;

public sealed class AiConversation
{
    private AiConversation()
    {
    }

    private AiConversation(
        EntityId id,
        EntityId accountId,
        EntityId? patientProfileId,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        PatientProfileId = patientProfileId;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId AccountId { get; private set; }

    public EntityId? PatientProfileId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt.HasValue;

    public static AiConversation Create(
        EntityId accountId,
        DateTimeOffset createdAt,
        EntityId? patientProfileId = null,
        EntityId? id = null)
    {
        AiGuard.EnsureId(accountId, nameof(accountId));
        if (patientProfileId.HasValue)
        {
            AiGuard.EnsureId(patientProfileId.Value, nameof(patientProfileId));
        }

        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        return new AiConversation(
            AiGuard.IdOrNew(id, nameof(id)),
            accountId,
            patientProfileId,
            createdAt);
    }

    public bool Delete(DateTimeOffset deletedAt)
    {
        InstantGuard.EnsureNotBefore(deletedAt, CreatedAt, nameof(deletedAt));
        if (DeletedAt.HasValue)
        {
            return false;
        }

        DeletedAt = deletedAt;
        return true;
    }
}
