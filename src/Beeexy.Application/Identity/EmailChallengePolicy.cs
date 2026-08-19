namespace Beeexy.Application.Identity;

public sealed record EmailChallengePolicy
{
    public EmailChallengePolicy(
        int codeLength,
        TimeSpan lifetime,
        int emailPermitLimit,
        int ipPermitLimit,
        TimeSpan rateLimitWindow)
    {
        if (codeLength is < 6 or > 9)
        {
            throw new ArgumentOutOfRangeException(
                nameof(codeLength),
                "OTP code length must be between 6 and 9 digits.");
        }

        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                "OTP lifetime must be positive and no longer than one hour.");
        }

        if (emailPermitLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(emailPermitLimit));
        }

        if (ipPermitLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ipPermitLimit));
        }

        if (rateLimitWindow <= TimeSpan.Zero || rateLimitWindow > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rateLimitWindow),
                "Rate-limit window must be positive and no longer than one day.");
        }

        CodeLength = codeLength;
        Lifetime = lifetime;
        EmailPermitLimit = emailPermitLimit;
        IpPermitLimit = ipPermitLimit;
        RateLimitWindow = rateLimitWindow;
    }

    public int CodeLength { get; }

    public TimeSpan Lifetime { get; }

    public int EmailPermitLimit { get; }

    public int IpPermitLimit { get; }

    public TimeSpan RateLimitWindow { get; }
}
