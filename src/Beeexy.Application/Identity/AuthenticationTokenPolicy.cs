namespace Beeexy.Application.Identity;

public sealed record AuthenticationTokenPolicy
{
    public AuthenticationTokenPolicy(
        string issuer,
        string audience,
        string signingKey,
        TimeSpan accessTokenLifetime,
        TimeSpan refreshTokenLifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);

        if (System.Text.Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new ArgumentException(
                "The access-token signing key must contain at least 32 bytes.",
                nameof(signingKey));
        }

        if (accessTokenLifetime <= TimeSpan.Zero ||
            accessTokenLifetime > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(accessTokenLifetime));
        }

        if (refreshTokenLifetime <= accessTokenLifetime ||
            refreshTokenLifetime > TimeSpan.FromDays(365))
        {
            throw new ArgumentOutOfRangeException(nameof(refreshTokenLifetime));
        }

        Issuer = issuer.Trim();
        Audience = audience.Trim();
        SigningKey = signingKey;
        AccessTokenLifetime = accessTokenLifetime;
        RefreshTokenLifetime = refreshTokenLifetime;
    }

    public string Issuer { get; }

    public string Audience { get; }

    public string SigningKey { get; }

    public TimeSpan AccessTokenLifetime { get; }

    public TimeSpan RefreshTokenLifetime { get; }
}
