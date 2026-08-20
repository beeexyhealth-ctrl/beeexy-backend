using Beeexy.Domain.Common;

namespace Beeexy.Domain.Identity;

public sealed class RefreshSession
{
    private RefreshSession()
    {
        RefreshTokenHash = null!;
    }

    private RefreshSession(
        EntityId id,
        EntityId accountId,
        EntityId familyId,
        EntityId? parentSessionId,
        TokenHash refreshTokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        FamilyId = familyId;
        ParentSessionId = parentSessionId;
        RefreshTokenHash = refreshTokenHash;
        ExpiresAt = expiresAt;
        Status = RefreshSessionStatus.Active;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId AccountId { get; private set; }

    public EntityId FamilyId { get; private set; }

    public EntityId? ParentSessionId { get; private set; }

    public EntityId? ReplacedBySessionId { get; private set; }

    public TokenHash RefreshTokenHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public RefreshSessionStatus Status { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset? RotatedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static RefreshSession Create(
        EntityId accountId,
        TokenHash refreshTokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt,
        EntityId? id = null,
        EntityId? familyId = null,
        EntityId? parentSessionId = null)
    {
        ArgumentNullException.ThrowIfNull(refreshTokenHash);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        InstantGuard.EnsureUtc(expiresAt, nameof(expiresAt));

        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "Refresh-session expiration must follow creation time.");
        }

        var sessionId = id ?? EntityId.New();
        return new RefreshSession(
            sessionId,
            accountId,
            familyId ?? sessionId,
            parentSessionId,
            refreshTokenHash,
            expiresAt,
            createdAt);
    }

    public bool IsExpiredAt(DateTimeOffset instant)
    {
        InstantGuard.EnsureUtc(instant, nameof(instant));
        return instant >= ExpiresAt;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        EnsureActive(revokedAt);
        Status = RefreshSessionStatus.Revoked;
        RevokedAt = revokedAt;
        UpdatedAt = revokedAt;
    }

    public void Rotate(EntityId successorSessionId, DateTimeOffset rotatedAt)
    {
        EnsureActive(rotatedAt);

        if (successorSessionId == Id)
        {
            throw new ArgumentException(
                "A refresh session cannot replace itself.",
                nameof(successorSessionId));
        }

        Status = RefreshSessionStatus.Revoked;
        ReplacedBySessionId = successorSessionId;
        RevokedAt = rotatedAt;
        RotatedAt = rotatedAt;
        UpdatedAt = rotatedAt;
    }

    public void MarkExpired(DateTimeOffset expiredAt)
    {
        EnsureActive(expiredAt);

        if (!IsExpiredAt(expiredAt))
        {
            throw new InvalidOperationException("A refresh session cannot expire before its expiration time.");
        }

        Status = RefreshSessionStatus.Expired;
        UpdatedAt = expiredAt;
    }

    private void EnsureActive(DateTimeOffset updatedAt)
    {
        InstantGuard.EnsureNotBefore(updatedAt, CreatedAt, nameof(updatedAt));

        if (Status != RefreshSessionStatus.Active)
        {
            throw new InvalidOperationException("Only an active refresh session can be changed.");
        }
    }
}
