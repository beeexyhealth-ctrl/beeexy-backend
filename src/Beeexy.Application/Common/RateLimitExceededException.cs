namespace Beeexy.Application.Common;

public sealed class RateLimitExceededException(TimeSpan retryAfter)
    : Exception("Too many requests were received. Please try again later.")
{
    public TimeSpan RetryAfter { get; } = retryAfter > TimeSpan.Zero
        ? retryAfter
        : TimeSpan.FromSeconds(1);
}
