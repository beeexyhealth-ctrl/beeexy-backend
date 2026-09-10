namespace Beeexy.Application.Sharing;

public sealed record ShareAccessTokenPolicy
{
    public const int AbsoluteMaximumLifetimeMinutes = 15;

    public ShareAccessTokenPolicy(
        string issuer,
        string audience,
        string signingKey,
        TimeSpan maximumLifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);
        if (System.Text.Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new ArgumentException(
                "The share-access signing key must contain at least 32 bytes.",
                nameof(signingKey));
        }

        if (maximumLifetime <= TimeSpan.Zero ||
            maximumLifetime > TimeSpan.FromMinutes(AbsoluteMaximumLifetimeMinutes))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLifetime));
        }

        Issuer = issuer.Trim();
        Audience = audience.Trim();
        SigningKey = signingKey;
        MaximumLifetime = maximumLifetime;
    }

    public string Issuer { get; }

    public string Audience { get; }

    public string SigningKey { get; }

    public TimeSpan MaximumLifetime { get; }
}

public sealed record ShareExchangeRateLimitPolicy
{
    public ShareExchangeRateLimitPolicy(int permitLimit, TimeSpan window)
    {
        if (permitLimit <= 0 || permitLimit > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(permitLimit));
        }

        if (window < TimeSpan.FromSeconds(1) || window > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        PermitLimit = permitLimit;
        Window = window;
    }

    public int PermitLimit { get; }

    public TimeSpan Window { get; }
}

public static class ShareAccessTokenClaims
{
    public const string CredentialType = "credential_type";
    public const string CredentialTypeValue = "share_access";
    public const string ShareGrantId = "share_grant_id";
    public const string ShareScope = "share_scope";
}
