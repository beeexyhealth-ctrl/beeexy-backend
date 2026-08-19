using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Infrastructure.Identity;

namespace Beeexy.Tests.Unit.Identity;

public sealed class EmailChallengeRateLimiterTests
{
    [Fact]
    public async Task EquivalentNormalizedEmailSharesThrottlePartition()
    {
        var clock = new MutableClock(UtcNow());
        var limiter = CreateLimiter(clock, emailLimit: 2, ipLimit: 10);

        var first = await limiter.TryAcquireAsync(
            NormalizedEmail.Create("Person@Example.COM"),
            "192.0.2.1");
        var second = await limiter.TryAcquireAsync(
            NormalizedEmail.Create(" person@example.com "),
            "192.0.2.2");
        var third = await limiter.TryAcquireAsync(
            NormalizedEmail.Create("person@example.com"),
            "192.0.2.3");

        Assert.True(first.IsAllowed);
        Assert.True(second.IsAllowed);
        Assert.False(third.IsAllowed);
        Assert.True(third.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task DifferentEmailsShareRequesterIpThrottlePartition()
    {
        var clock = new MutableClock(UtcNow());
        var limiter = CreateLimiter(clock, emailLimit: 10, ipLimit: 2);

        Assert.True((await limiter.TryAcquireAsync(
            NormalizedEmail.Create("first@example.com"),
            "192.0.2.10")).IsAllowed);
        Assert.True((await limiter.TryAcquireAsync(
            NormalizedEmail.Create("second@example.com"),
            "192.0.2.10")).IsAllowed);

        var rejected = await limiter.TryAcquireAsync(
            NormalizedEmail.Create("third@example.com"),
            "192.0.2.10");

        Assert.False(rejected.IsAllowed);
    }

    [Fact]
    public async Task PermitWindowResetsAfterConfiguredDuration()
    {
        var clock = new MutableClock(UtcNow());
        var limiter = CreateLimiter(clock, emailLimit: 1, ipLimit: 10);
        var email = NormalizedEmail.Create("person@example.com");

        Assert.True((await limiter.TryAcquireAsync(email, "192.0.2.1")).IsAllowed);
        Assert.False((await limiter.TryAcquireAsync(email, "192.0.2.1")).IsAllowed);

        clock.UtcNow = clock.UtcNow.AddMinutes(15);

        Assert.True((await limiter.TryAcquireAsync(email, "192.0.2.1")).IsAllowed);
    }

    private static InMemoryEmailChallengeRateLimiter CreateLimiter(
        IClock clock,
        int emailLimit,
        int ipLimit)
    {
        return new InMemoryEmailChallengeRateLimiter(
            clock,
            new EmailChallengePolicy(
                6,
                TimeSpan.FromMinutes(10),
                emailLimit,
                ipLimit,
                TimeSpan.FromMinutes(15)));
    }

    private static DateTimeOffset UtcNow()
    {
        return new DateTimeOffset(2026, 8, 20, 2, 0, 0, TimeSpan.Zero);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
