using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public interface IEmailChallengeRateLimiter
{
    ValueTask<EmailChallengeRateLimitResult> TryAcquireAsync(
        NormalizedEmail email,
        string requesterIpAddress,
        CancellationToken cancellationToken = default);
}

public readonly record struct EmailChallengeRateLimitResult(
    bool IsAllowed,
    TimeSpan? RetryAfter)
{
    public static EmailChallengeRateLimitResult Allowed => new(true, null);

    public static EmailChallengeRateLimitResult Rejected(TimeSpan retryAfter)
    {
        return new EmailChallengeRateLimitResult(false, retryAfter);
    }
}
