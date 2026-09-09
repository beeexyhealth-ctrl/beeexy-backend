using Beeexy.Domain.Common;

namespace Beeexy.Domain.Sharing;

public sealed class ShareGrantItem
{
    private ShareGrantItem()
    {
        ResourceType = null!;
    }

    private ShareGrantItem(
        EntityId id,
        EntityId shareGrantId,
        ShareResourceType resourceType,
        EntityId resourceId,
        DateTimeOffset createdAt)
    {
        Id = id;
        ShareGrantId = shareGrantId;
        ResourceType = resourceType;
        ResourceId = resourceId;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId ShareGrantId { get; private set; }

    public ShareResourceType ResourceType { get; private set; }

    public EntityId ResourceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ShareGrantItem Create(
        ShareGrant shareGrant,
        ShareResourceType resourceType,
        EntityId resourceId,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(shareGrant);
        ArgumentNullException.ThrowIfNull(resourceType);
        SharingGuard.EnsureId(resourceId, nameof(resourceId));
        InstantGuard.EnsureNotBefore(createdAt, shareGrant.CreatedAt, nameof(createdAt));

        return new ShareGrantItem(
            SharingGuard.IdOrNew(id, nameof(id)),
            shareGrant.Id,
            resourceType,
            resourceId,
            createdAt);
    }
}
