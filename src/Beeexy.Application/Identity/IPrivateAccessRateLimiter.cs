namespace Beeexy.Application.Identity;

public interface IPrivateAccessRateLimiter
{
    Task<PrivateAccessRateLimitDecision> TryAcquireAsync(
        string requesterIpAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public readonly record struct PrivateAccessRateLimitDecision(
    bool IsAllowed,
    TimeSpan RetryAfter)
{
    public static PrivateAccessRateLimitDecision Allowed => new(true, TimeSpan.Zero);
    public static PrivateAccessRateLimitDecision Rejected(TimeSpan retryAfter) =>
        new(false, retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromSeconds(1));
}
