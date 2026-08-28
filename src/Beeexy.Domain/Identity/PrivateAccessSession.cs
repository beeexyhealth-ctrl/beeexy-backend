using Beeexy.Domain.Common;

namespace Beeexy.Domain.Identity;

public sealed class PrivateAccessSession
{
    private PrivateAccessSession()
    {
        TokenHash = null!;
    }

    private PrivateAccessSession(
        EntityId id,
        EntityId credentialId,
        EntityId rootRefreshSessionId,
        TokenHash tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt)
    {
        Id = id;
        CredentialId = credentialId;
        RootRefreshSessionId = rootRefreshSessionId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        Status = PrivateAccessSessionStatus.Active;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }
    public EntityId CredentialId { get; private set; }
    public EntityId RootRefreshSessionId { get; private set; }
    public TokenHash TokenHash { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public PrivateAccessSessionStatus Status { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static PrivateAccessSession Create(
        EntityId credentialId,
        EntityId rootRefreshSessionId,
        TokenHash tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        InstantGuard.EnsureUtc(expiresAt, nameof(expiresAt));
        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        return new PrivateAccessSession(
            id ?? EntityId.New(),
            credentialId,
            rootRefreshSessionId,
            tokenHash,
            expiresAt,
            createdAt);
    }

    public bool IsExpiredAt(DateTimeOffset instant) => instant >= ExpiresAt;

    public void Revoke(DateTimeOffset updatedAt)
    {
        EnsureActive(updatedAt);
        Status = PrivateAccessSessionStatus.Revoked;
        RevokedAt = updatedAt;
        UpdatedAt = updatedAt;
    }

    public void MarkExpired(DateTimeOffset updatedAt)
    {
        EnsureActive(updatedAt);
        if (!IsExpiredAt(updatedAt))
        {
            throw new InvalidOperationException("The private-access session has not expired.");
        }

        Status = PrivateAccessSessionStatus.Expired;
        UpdatedAt = updatedAt;
    }

    private void EnsureActive(DateTimeOffset updatedAt)
    {
        InstantGuard.EnsureNotBefore(updatedAt, CreatedAt, nameof(updatedAt));
        if (Status != PrivateAccessSessionStatus.Active)
        {
            throw new InvalidOperationException("Only an active private-access session can change.");
        }
    }
}
