using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;
using System.IdentityModel.Tokens.Jwt;

namespace Beeexy.Api.Sharing;

internal static class ShareAccessAuthenticationDefaults
{
    public const string Scheme = "ShareAccess";
    public const string ReadOnlyPolicy = "ShareAccessReadOnly";

    public static bool HasRequiredClaims(System.Security.Claims.ClaimsPrincipal? principal)
    {
        var credentialType = principal?.FindFirst(ShareAccessTokenClaims.CredentialType)?.Value;
        var grantId = principal?.FindFirst(ShareAccessTokenClaims.ShareGrantId)?.Value;
        var scope = principal?.FindFirst(ShareAccessTokenClaims.ShareScope)?.Value;
        var tokenId = principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        return string.Equals(
                credentialType,
                ShareAccessTokenClaims.CredentialTypeValue,
                StringComparison.Ordinal) &&
            Guid.TryParse(grantId, out var parsedGrantId) &&
            parsedGrantId != Guid.Empty &&
            Guid.TryParse(tokenId, out var parsedTokenId) &&
            parsedTokenId != Guid.Empty &&
            Enum.TryParse<ShareScope>(scope, false, out var parsedScope) &&
            parsedScope is ShareScope.FullProfile or
                ShareScope.PreTriage or
                ShareScope.SpecificRecords;
    }

    public static bool TryGetIdentity(
        System.Security.Claims.ClaimsPrincipal? principal,
        out ShareAccessIdentity identity)
    {
        identity = null!;
        if (!HasRequiredClaims(principal) ||
            !Guid.TryParse(
                principal!.FindFirst(ShareAccessTokenClaims.ShareGrantId)?.Value,
                out var grantId) ||
            !Guid.TryParse(
                principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value,
                out var tokenId) ||
            !Enum.TryParse<ShareScope>(
                principal.FindFirst(ShareAccessTokenClaims.ShareScope)?.Value,
                false,
                out var scope))
        {
            return false;
        }

        identity = new ShareAccessIdentity(
            EntityId.From(grantId),
            scope,
            EntityId.From(tokenId));
        return true;
    }
}
