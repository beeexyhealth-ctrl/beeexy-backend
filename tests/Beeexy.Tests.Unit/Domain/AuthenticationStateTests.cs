using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Tests.Unit.Domain;

public sealed class AuthenticationStateTests
{
    [Fact]
    public void Challenge_TracksAttemptsAndCanBeConsumedOnce()
    {
        var createdAt = Utc(12);
        var challenge = CreateChallenge(createdAt);

        challenge.RecordFailedAttempt(createdAt.AddSeconds(10));
        challenge.Consume(createdAt.AddMinutes(1));

        Assert.Equal(1, challenge.AttemptCount);
        Assert.Equal(ChallengeStatus.Consumed, challenge.Status);
        Assert.Equal(createdAt.AddMinutes(1), challenge.ConsumedAt);
        Assert.Throws<InvalidOperationException>(() =>
            challenge.Consume(createdAt.AddMinutes(2)));
    }

    [Fact]
    public void Challenge_CanOnlyExpireAtOrAfterItsExpiration()
    {
        var createdAt = Utc(12);
        var challenge = CreateChallenge(createdAt);

        Assert.False(challenge.IsExpiredAt(createdAt.AddMinutes(4)));
        Assert.Throws<InvalidOperationException>(() =>
            challenge.MarkExpired(createdAt.AddMinutes(4)));

        challenge.MarkExpired(createdAt.AddMinutes(5));

        Assert.True(challenge.IsExpiredAt(createdAt.AddMinutes(5)));
        Assert.Equal(ChallengeStatus.Expired, challenge.Status);
        Assert.Null(challenge.ConsumedAt);
    }

    [Fact]
    public void Challenge_RejectsConsumptionAfterExpiration()
    {
        var createdAt = Utc(12);
        var challenge = CreateChallenge(createdAt);

        Assert.Throws<InvalidOperationException>(() =>
            challenge.Consume(createdAt.AddMinutes(5)));
        Assert.Equal(ChallengeStatus.Pending, challenge.Status);
    }

    [Fact]
    public void RefreshSession_CanBeRevokedOnce()
    {
        var createdAt = Utc(12);
        var session = CreateSession(createdAt);

        session.Revoke(createdAt.AddMinutes(1));

        Assert.Equal(RefreshSessionStatus.Revoked, session.Status);
        Assert.Equal(createdAt.AddMinutes(1), session.RevokedAt);
        Assert.Throws<InvalidOperationException>(() =>
            session.MarkExpired(createdAt.AddHours(1)));
    }

    [Fact]
    public void RefreshSession_CanOnlyExpireAtOrAfterItsExpiration()
    {
        var createdAt = Utc(12);
        var session = CreateSession(createdAt);

        Assert.Throws<InvalidOperationException>(() =>
            session.MarkExpired(createdAt.AddMinutes(30)));

        session.MarkExpired(createdAt.AddHours(1));

        Assert.Equal(RefreshSessionStatus.Expired, session.Status);
        Assert.Null(session.RevokedAt);
    }

    private static EmailAuthenticationChallenge CreateChallenge(DateTimeOffset createdAt)
    {
        return EmailAuthenticationChallenge.Create(
            NormalizedEmail.Create("person@example.com"),
            TokenHash.FromHash("otp-hash"),
            createdAt.AddMinutes(5),
            createdAt,
            EntityId.New());
    }

    private static RefreshSession CreateSession(DateTimeOffset createdAt)
    {
        return RefreshSession.Create(
            EntityId.New(),
            TokenHash.FromHash("refresh-hash"),
            createdAt.AddHours(1),
            createdAt,
            EntityId.New());
    }

    private static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(2026, 8, 19, hour, 0, 0, TimeSpan.Zero);
    }
}
