using System.Collections.Concurrent;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Infrastructure.Identity;

public sealed class InMemoryEmailChallengeRateLimiter(
    IClock clock,
    EmailChallengePolicy policy) : IEmailChallengeRateLimiter
{
    private readonly ConcurrentDictionary<string, WindowCounter> _counters = new();
    private long _acquisitionCount;

    public ValueTask<EmailChallengeRateLimitResult> TryAcquireAsync(
        NormalizedEmail email,
        string requesterIpAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterIpAddress);
        cancellationToken.ThrowIfCancellationRequested();

        var now = clock.UtcNow;
        var emailResult = TryAcquire(
            $"email:{email.Value}",
            policy.EmailPermitLimit,
            now);
        if (!emailResult.IsAllowed)
        {
            return ValueTask.FromResult(emailResult);
        }

        var ipResult = TryAcquire(
            $"ip:{requesterIpAddress}",
            policy.IpPermitLimit,
            now);

        if (Interlocked.Increment(ref _acquisitionCount) % 256 == 0)
        {
            RemoveExpiredCounters(now);
        }

        return ValueTask.FromResult(ipResult);
    }

    private EmailChallengeRateLimitResult TryAcquire(
        string partitionKey,
        int permitLimit,
        DateTimeOffset now)
    {
        var counter = _counters.GetOrAdd(
            partitionKey,
            _ => new WindowCounter(now.Add(policy.RateLimitWindow)));

        lock (counter.SyncRoot)
        {
            if (now >= counter.WindowEndsAt)
            {
                counter.Count = 1;
                counter.WindowEndsAt = now.Add(policy.RateLimitWindow);
                return EmailChallengeRateLimitResult.Allowed;
            }

            if (counter.Count >= permitLimit)
            {
                var retryAfter = counter.WindowEndsAt - now;
                return EmailChallengeRateLimitResult.Rejected(
                    retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromSeconds(1));
            }

            counter.Count++;
            return EmailChallengeRateLimitResult.Allowed;
        }
    }

    private void RemoveExpiredCounters(DateTimeOffset now)
    {
        foreach (var entry in _counters)
        {
            if (entry.Value.WindowEndsAt <= now)
            {
                _counters.TryRemove(entry.Key, out _);
            }
        }
    }

    private sealed class WindowCounter(DateTimeOffset windowEndsAt)
    {
        public object SyncRoot { get; } = new();

        public int Count { get; set; }

        public DateTimeOffset WindowEndsAt { get; set; } = windowEndsAt;
    }
}
