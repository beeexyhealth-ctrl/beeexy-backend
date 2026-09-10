using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;
using Microsoft.IdentityModel.Tokens;

namespace Beeexy.Infrastructure.Sharing;

public sealed class JwtShareAccessTokenIssuer(ShareAccessTokenPolicy policy)
    : IShareAccessTokenIssuer
{
    public IssuedShareAccessToken Issue(
        EntityId shareGrantId,
        ShareScope scope,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        if (shareGrantId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty share grant identifier is required.",
                nameof(shareGrantId));
        }

        if (scope is not (ShareScope.FullProfile or
            ShareScope.PreTriage or
            ShareScope.SpecificRecords))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        if (expiresAt <= issuedAt || expiresAt > issuedAt.Add(policy.MaximumLifetime))
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(policy.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(
                ShareAccessTokenClaims.CredentialType,
                ShareAccessTokenClaims.CredentialTypeValue),
            new Claim(
                ShareAccessTokenClaims.ShareGrantId,
                shareGrantId.Value.ToString("D")),
            new Claim(ShareAccessTokenClaims.ShareScope, scope.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                EpochTime.GetIntDate(issuedAt.UtcDateTime)
                    .ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };
        var token = new JwtSecurityToken(
            policy.Issuer,
            policy.Audience,
            claims,
            issuedAt.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);

        return new IssuedShareAccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }
}
