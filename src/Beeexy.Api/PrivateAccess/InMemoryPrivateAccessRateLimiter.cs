using System.Collections.Concurrent;

namespace Beeexy.Api.PrivateAccess;

internal sealed class InMemoryPrivateAccessRateLimiter(PrivateAccessSettings settings)
{
    private readonly ConcurrentDictionary<string, WindowCounter> _counters = new();
    private long _acquisitionCount;

    public PrivateAccessRateLimitResult TryAcquire(string requesterIpAddress, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterIpAddress);
        if (!settings.Enabled)
        {
            return PrivateAccessRateLimitResult.Allowed;
        }

        var counter = _counters.GetOrAdd(
            requesterIpAddress,
            _ => new WindowCounter(now.Add(settings.LoginRateLimitWindow)));

        PrivateAccessRateLimitResult result;
        lock (counter.SyncRoot)
        {
            if (now >= counter.WindowEndsAt)
            {
                counter.Count = 1;
                counter.WindowEndsAt = now.Add(settings.LoginRateLimitWindow);
                result = PrivateAccessRateLimitResult.Allowed;
            }
            else if (counter.Count >= settings.LoginPermitLimit)
            {
                result = PrivateAccessRateLimitResult.Rejected(counter.WindowEndsAt - now);
            }
            else
            {
                counter.Count++;
                result = PrivateAccessRateLimitResult.Allowed;
            }
        }

        if (Interlocked.Increment(ref _acquisitionCount) % 256 == 0)
        {
            foreach (var entry in _counters)
            {
                if (entry.Value.WindowEndsAt <= now)
                {
                    _counters.TryRemove(entry.Key, out _);
                }
            }
        }

        return result;
    }

    private sealed class WindowCounter(DateTimeOffset windowEndsAt)
    {
        public object SyncRoot { get; } = new();
        public int Count { get; set; }
        public DateTimeOffset WindowEndsAt { get; set; } = windowEndsAt;
    }
}

internal readonly record struct PrivateAccessRateLimitResult(bool IsAllowed, TimeSpan RetryAfter)
{
    public static PrivateAccessRateLimitResult Allowed => new(true, TimeSpan.Zero);

    public static PrivateAccessRateLimitResult Rejected(TimeSpan retryAfter) =>
        new(false, retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromSeconds(1));
}
