using Beeexy.Domain.Common;

namespace Beeexy.Domain.Identity;

public sealed class EmailAuthenticationChallenge
{
    private EmailAuthenticationChallenge()
    {
        Email = null!;
        OtpHash = null!;
    }

    private EmailAuthenticationChallenge(
        EntityId id,
        NormalizedEmail email,
        TokenHash otpHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        OtpHash = otpHash;
        ExpiresAt = expiresAt;
        Status = ChallengeStatus.Pending;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public NormalizedEmail Email { get; private set; }

    public TokenHash OtpHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int AttemptCount { get; private set; }

    public ChallengeStatus Status { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static EmailAuthenticationChallenge Create(
        NormalizedEmail email,
        TokenHash otpHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(otpHash);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        InstantGuard.EnsureUtc(expiresAt, nameof(expiresAt));

        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "Challenge expiration must follow creation time.");
        }

        return new EmailAuthenticationChallenge(
            id ?? EntityId.New(),
            email,
            otpHash,
            expiresAt,
            createdAt);
    }

    public bool IsExpiredAt(DateTimeOffset instant)
    {
        InstantGuard.EnsureUtc(instant, nameof(instant));
        return instant >= ExpiresAt;
    }

    public void RecordFailedAttempt(DateTimeOffset updatedAt)
    {
        EnsurePending(updatedAt);
        AttemptCount = checked(AttemptCount + 1);
        UpdatedAt = updatedAt;
    }

    public void Consume(DateTimeOffset consumedAt)
    {
        EnsurePending(consumedAt);

        if (IsExpiredAt(consumedAt))
        {
            throw new InvalidOperationException("An expired challenge cannot be consumed.");
        }

        Status = ChallengeStatus.Consumed;
        ConsumedAt = consumedAt;
        UpdatedAt = consumedAt;
    }

    public void MarkExpired(DateTimeOffset expiredAt)
    {
        EnsurePending(expiredAt);

        if (!IsExpiredAt(expiredAt))
        {
            throw new InvalidOperationException("A challenge cannot expire before its expiration time.");
        }

        Status = ChallengeStatus.Expired;
        UpdatedAt = expiredAt;
    }

    private void EnsurePending(DateTimeOffset updatedAt)
    {
        InstantGuard.EnsureNotBefore(updatedAt, CreatedAt, nameof(updatedAt));

        if (Status != ChallengeStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending challenge can be changed.");
        }
    }
}
