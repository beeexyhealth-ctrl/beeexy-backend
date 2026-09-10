using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;
using Beeexy.Infrastructure.Sharing;
using Microsoft.IdentityModel.Tokens;

namespace Beeexy.Tests.Unit.Sharing;

public sealed class ShareAccessTokenSecurityTests
{
    private const string SigningKey =
        "unit-test-share-signing-key-with-at-least-32-bytes";

    [Fact]
    [Trait("Category", "Phase113")]
    public void Token_IsGrantBoundReadOnlyAndContainsOnlyAllowListedClaims()
    {
        var issuedAt = new DateTimeOffset(2026, 9, 9, 20, 0, 0, TimeSpan.Zero);
        var grantId = EntityId.New();
        var policy = Policy();

        var issued = new JwtShareAccessTokenIssuer(policy).Issue(
            grantId,
            ShareScope.PreTriage,
            issuedAt,
            issuedAt.AddMinutes(15));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.Value);

        Assert.Equal("HS256", jwt.Header.Alg);
        Assert.Equal(policy.Issuer, jwt.Issuer);
        Assert.Equal([policy.Audience], jwt.Audiences);
        Assert.Equal(issuedAt.AddMinutes(15), issued.ExpiresAt);
        Assert.Equal(
            ShareAccessTokenClaims.CredentialTypeValue,
            jwt.Claims.Single(claim =>
                claim.Type == ShareAccessTokenClaims.CredentialType).Value);
        Assert.Equal(
            grantId.Value.ToString("D"),
            jwt.Claims.Single(claim =>
                claim.Type == ShareAccessTokenClaims.ShareGrantId).Value);
        Assert.Equal(
            "PreTriage",
            jwt.Claims.Single(claim =>
                claim.Type == ShareAccessTokenClaims.ShareScope).Value);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type is "sub" or "sid");
        Assert.DoesNotContain(jwt.Claims, claim =>
            claim.Type.Contains("account", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Contains("patient", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Contains("beeexy", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Contains("capability", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Contains("clinical", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Phase113")]
    public void ShareToken_ValidatesOnlyForDistinctShareAudience()
    {
        var now = DateTimeOffset.UtcNow;
        var policy = Policy();
        var issued = new JwtShareAccessTokenIssuer(policy).Issue(
            EntityId.New(),
            ShareScope.FullProfile,
            now,
            now.AddMinutes(15));
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        var principal = handler.ValidateToken(
            issued.Value,
            ValidationParameters(policy.Audience),
            out _);

        Assert.Equal(
            ShareAccessTokenClaims.CredentialTypeValue,
            principal.FindFirst(ShareAccessTokenClaims.CredentialType)?.Value);
        Assert.Throws<SecurityTokenInvalidAudienceException>(() => handler.ValidateToken(
            issued.Value,
            ValidationParameters("unit-test-account-audience"),
            out _));
    }

    [Fact]
    [Trait("Category", "Phase113")]
    public void Issuer_RejectsLifetimeBeyondConfiguredMaximum()
    {
        var now = DateTimeOffset.UtcNow;
        var issuer = new JwtShareAccessTokenIssuer(Policy());

        Assert.Throws<ArgumentOutOfRangeException>(() => issuer.Issue(
            EntityId.New(),
            ShareScope.FullProfile,
            now,
            now.AddMinutes(15).AddTicks(1)));
    }

    [Theory]
    [Trait("Category", "Phase113")]
    [InlineData(ShareScope.Case)]
    [InlineData(ShareScope.Visit)]
    public void Issuer_RejectsReservedNonExecutableScope(ShareScope scope)
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new JwtShareAccessTokenIssuer(Policy()).Issue(
                EntityId.New(),
                scope,
                now,
                now.AddMinutes(15)));
    }

    private static ShareAccessTokenPolicy Policy() => new(
        "unit-test-issuer",
        "unit-test-share-audience",
        SigningKey,
        TimeSpan.FromMinutes(15));

    private static TokenValidationParameters ValidationParameters(string audience) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
        ValidateIssuer = true,
        ValidIssuer = "unit-test-issuer",
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ClockSkew = TimeSpan.Zero
    };
}
