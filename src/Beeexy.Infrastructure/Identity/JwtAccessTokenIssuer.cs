using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Microsoft.IdentityModel.Tokens;

namespace Beeexy.Infrastructure.Identity;

public sealed class JwtAccessTokenIssuer(AuthenticationTokenPolicy policy)
    : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(
        EntityId accountId,
        EntityId sessionId,
        DateTimeOffset issuedAt)
    {
        var expiresAt = issuedAt.Add(policy.AccessTokenLifetime);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(policy.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, accountId.Value.ToString("D")),
            new Claim("sid", sessionId.Value.ToString("D")),
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

        return new IssuedAccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }
}
