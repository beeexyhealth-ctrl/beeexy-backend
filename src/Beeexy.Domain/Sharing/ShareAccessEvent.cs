using Beeexy.Domain.Common;

namespace Beeexy.Domain.Sharing;

public sealed class ShareAccessEvent
{
    private ShareAccessEvent()
    {
    }

    private ShareAccessEvent(
        EntityId id,
        EntityId shareGrantId,
        ShareAccessEventType eventType,
        ShareAccessOutcome outcome,
        ShareResourceType? resourceType,
        DateTimeOffset occurredAt)
    {
        Id = id;
        ShareGrantId = shareGrantId;
        EventType = eventType;
        Outcome = outcome;
        ResourceType = resourceType;
        OccurredAt = occurredAt;
    }

    public EntityId Id { get; private set; }

    public EntityId ShareGrantId { get; private set; }

    public ShareAccessEventType EventType { get; private set; }

    public ShareAccessOutcome Outcome { get; private set; }

    public ShareResourceType? ResourceType { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static ShareAccessEvent Create(
        ShareGrant shareGrant,
        ShareAccessEventType eventType,
        ShareAccessOutcome outcome,
        DateTimeOffset occurredAt,
        ShareResourceType? resourceType = null,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(shareGrant);
        SharingGuard.EnsureDefined(eventType, nameof(eventType));
        SharingGuard.EnsureDefined(outcome, nameof(outcome));
        InstantGuard.EnsureNotBefore(occurredAt, shareGrant.CreatedAt, nameof(occurredAt));

        return new ShareAccessEvent(
            SharingGuard.IdOrNew(id, nameof(id)),
            shareGrant.Id,
            eventType,
            outcome,
            resourceType,
            occurredAt);
    }
}
