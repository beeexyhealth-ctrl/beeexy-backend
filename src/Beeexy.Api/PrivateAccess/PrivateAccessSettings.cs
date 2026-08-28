using Beeexy.Application.Identity;

namespace Beeexy.Api.PrivateAccess;

internal sealed record PrivateAccessSettings(
    bool Enabled,
    PrivateAccessAuthenticationMode AuthenticationMode,
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

    public DemoGuestSettings DemoGuest { get; init; } = DemoGuestSettings.Disabled;

    public static PrivateAccessSettings Disabled { get; } = new(
        false,
        PrivateAccessAuthenticationMode.Legacy,
        null,
        null,
        null,
        null,
        TimeSpan.Zero,
        0,
        TimeSpan.Zero,
        false);
}

internal enum PrivateAccessAuthenticationMode
{
    Legacy = 1,
    Database = 2
}

internal sealed record DemoGuestSettings(bool Enabled, DemoGuestDefinition? Definition)
{
    public static DemoGuestSettings Disabled { get; } = new(false, null);
}
