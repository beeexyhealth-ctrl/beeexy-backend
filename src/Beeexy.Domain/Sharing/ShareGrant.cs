using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Domain.Sharing;

public sealed class ShareGrant
{
    private ShareGrant()
    {
        CapabilityHash = null!;
    }

    private ShareGrant(
        EntityId id,
        EntityId patientProfileId,
        EntityId creatorAccountId,
        EntityId idempotencyKey,
        ShareRequestFingerprint requestFingerprint,
        ShareScope scope,
        TokenHash capabilityHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        PatientProfileId = patientProfileId;
        CreatorAccountId = creatorAccountId;
        IdempotencyKey = idempotencyKey;
        RequestFingerprint = requestFingerprint;
        Scope = scope;
        CapabilityHash = capabilityHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Version = 1;
    }

    public EntityId Id { get; private set; }

    public EntityId PatientProfileId { get; private set; }

    public EntityId CreatorAccountId { get; private set; }

    public EntityId IdempotencyKey { get; private set; }

    public ShareRequestFingerprint RequestFingerprint { get; private set; } = null!;

    public ShareScope Scope { get; private set; }

    public TokenHash CapabilityHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public EntityId? RevokedByAccountId { get; private set; }

    public long Version { get; private set; }

    public static ShareGrant Create(
        EntityId patientProfileId,
        EntityId creatorAccountId,
        EntityId idempotencyKey,
        ShareRequestFingerprint requestFingerprint,
        ShareScope scope,
        TokenHash capabilityHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        EntityId? id = null)
    {
        SharingGuard.EnsureId(patientProfileId, nameof(patientProfileId));
        SharingGuard.EnsureId(creatorAccountId, nameof(creatorAccountId));
        SharingGuard.EnsureId(idempotencyKey, nameof(idempotencyKey));
        ArgumentNullException.ThrowIfNull(requestFingerprint);
        SharingGuard.EnsureDefined(scope, nameof(scope));
        ArgumentNullException.ThrowIfNull(capabilityHash);
        if (capabilityHash.Value.Length < SharingPersistenceLimits.CapabilityHashMinimum)
        {
            throw new ArgumentException(
                $"The capability hash must contain at least " +
                $"{SharingPersistenceLimits.CapabilityHashMinimum} characters.",
                nameof(capabilityHash));
        }

        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        InstantGuard.EnsureUtc(expiresAt, nameof(expiresAt));
        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "Share expiration must follow creation time.");
        }

        return new ShareGrant(
            SharingGuard.IdOrNew(id, nameof(id)),
            patientProfileId,
            creatorAccountId,
            idempotencyKey,
            requestFingerprint,
            scope,
            capabilityHash,
            createdAt,
            expiresAt);
    }

    public ShareGrantStatus GetStatus(DateTimeOffset at)
    {
        InstantGuard.EnsureNotBefore(at, CreatedAt, nameof(at));
        if (RevokedAt.HasValue)
        {
            return ShareGrantStatus.Revoked;
        }

        return at >= ExpiresAt
            ? ShareGrantStatus.Expired
            : ShareGrantStatus.Active;
    }

    public bool IsActiveAt(DateTimeOffset at)
    {
        return GetStatus(at) == ShareGrantStatus.Active;
    }

    public bool Revoke(EntityId revokedByAccountId, DateTimeOffset revokedAt)
    {
        SharingGuard.EnsureId(revokedByAccountId, nameof(revokedByAccountId));
        InstantGuard.EnsureNotBefore(revokedAt, CreatedAt, nameof(revokedAt));
        if (RevokedAt.HasValue)
        {
            return false;
        }

        RevokedAt = revokedAt;
        RevokedByAccountId = revokedByAccountId;
        Version++;
        return true;
    }
}
