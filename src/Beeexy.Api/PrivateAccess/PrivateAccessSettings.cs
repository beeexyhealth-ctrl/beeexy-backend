namespace Beeexy.Api.PrivateAccess;

internal sealed record PrivateAccessSettings(
    bool Enabled,
    string? Username,
    string? PasswordHash,
    string? KeywordHash,
    byte[]? SessionSigningKey,
    TimeSpan SessionLifetime,
    int LoginPermitLimit,
    TimeSpan LoginRateLimitWindow,
    bool SecureCookie)
{
    public const string CookieName = "beeexy-private-access";

    public static PrivateAccessSettings Disabled { get; } = new(
        false,
        null,
        null,
        null,
        null,
        TimeSpan.Zero,
        0,
        TimeSpan.Zero,
        false);
}
