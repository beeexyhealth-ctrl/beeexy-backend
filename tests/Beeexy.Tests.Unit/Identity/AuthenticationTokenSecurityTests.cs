using System.IdentityModel.Tokens.Jwt;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Identity;

namespace Beeexy.Tests.Unit.Identity;

public sealed class AuthenticationTokenSecurityTests
{
    [Fact]
    public void AccessToken_IsSignedAndContainsOnlyStableAuthenticationClaims()
    {
        var issuedAt = new DateTimeOffset(2026, 8, 20, 2, 0, 0, TimeSpan.Zero);
        var accountId = EntityId.New();
        var sessionId = EntityId.New();
        var policy = Policy();
        var issuer = new JwtAccessTokenIssuer(policy);

        var issued = issuer.Issue(accountId, sessionId, issuedAt);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.Value);

        Assert.Equal("HS256", jwt.Header.Alg);
        Assert.Equal(policy.Issuer, jwt.Issuer);
        Assert.Equal([policy.Audience], jwt.Audiences);
        Assert.Equal(accountId.Value.ToString("D"), jwt.Subject);
        Assert.Equal(
            sessionId.Value.ToString("D"),
            jwt.Claims.Single(claim => claim.Type == "sid").Value);
        Assert.Equal(issuedAt.AddMinutes(15), issued.ExpiresAt);
        Assert.DoesNotContain(jwt.Claims, claim =>
            claim.Type.Contains("email", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Contains("beeexy", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Contains("patient", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RefreshTokens_AreRandomOpaqueAndOnlyDeterministicHashIsPersistable()
    {
        var service = new CryptographicRefreshTokenService();

        var first = service.Generate();
        var second = service.Generate();

        Assert.StartsWith("rt1.", first.Value, StringComparison.Ordinal);
        Assert.NotEqual(first.Value, second.Value);
        Assert.Equal(first.Hash, service.Hash(first.Value));
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.StartsWith("sha256:", first.Hash.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(first.Value, first.Hash.Value, StringComparison.Ordinal);
    }

    private static AuthenticationTokenPolicy Policy() => new(
        "unit-test-issuer",
        "unit-test-audience",
        "unit-test-signing-key-with-at-least-32-bytes",
        TimeSpan.FromMinutes(15),
        TimeSpan.FromDays(30));
}
