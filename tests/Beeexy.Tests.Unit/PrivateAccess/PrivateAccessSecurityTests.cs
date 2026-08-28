using Beeexy.Api.PrivateAccess;
using Microsoft.AspNetCore.Http;

namespace Beeexy.Tests.Unit.PrivateAccess;

public sealed class PrivateAccessSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PasswordHash_RoundTripsAndRejectsWrongSecret()
    {
        var encoded = PrivateAccessPasswordHasher.Hash("StrongPassword!123");

        Assert.True(PrivateAccessPasswordHasher.IsValidEncodedHash(encoded));
        Assert.True(PrivateAccessPasswordHasher.Verify("StrongPassword!123", encoded));
        Assert.False(PrivateAccessPasswordHasher.Verify("wrong", encoded));
        Assert.DoesNotContain("StrongPassword!123", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionToken_RecognizesValidAndRejectsExpiredOrTamperedTokens()
    {
        var service = new PrivateAccessSessionTokenService(Settings());
        var session = service.Issue(Now);

        Assert.True(service.TryValidate(session.Token, Now, out var expiresAt));
        Assert.Equal(Now.AddMinutes(30), expiresAt);
        Assert.False(service.TryValidate(session.Token, expiresAt, out _));

        var replacement = session.Token[^1] == 'A' ? 'B' : 'A';
        var tampered = session.Token[..^1] + replacement;
        Assert.False(service.TryValidate(tampered, Now, out _));
    }

    [Fact]
    public void ProductionCookie_IsSecureHttpOnlyAndCrossSiteCompatible()
    {
        var options = PrivateAccessEndpointExtensions.CreateCookieOptions(
            Settings(),
            Now.AddMinutes(30));

        Assert.True(options.HttpOnly);
        Assert.True(options.Secure);
        Assert.Equal(SameSiteMode.None, options.SameSite);
        Assert.Equal("/", options.Path);
        Assert.Equal(Now.AddMinutes(30), options.Expires);
    }

    private static PrivateAccessSettings Settings() => new(
        true,
        PrivateAccessAuthenticationMode.Legacy,
        "BeeexyHealth",
        PrivateAccessPasswordHasher.Hash("StrongPassword!123"),
        PrivateAccessPasswordHasher.Hash("HealthTech"),
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
        TimeSpan.FromMinutes(30),
        5,
        TimeSpan.FromMinutes(15),
        true);
}
