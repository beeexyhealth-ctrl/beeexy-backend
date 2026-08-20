namespace Beeexy.Api.Configuration;

internal sealed record GoogleAuthenticationStartupSettings(
    bool Enabled,
    string? ClientId);
