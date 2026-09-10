using Beeexy.Application.Sharing;
using Beeexy.Domain.Sharing;

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
        return string.Equals(
                credentialType,
                ShareAccessTokenClaims.CredentialTypeValue,
                StringComparison.Ordinal) &&
            Guid.TryParse(grantId, out var parsedGrantId) &&
            parsedGrantId != Guid.Empty &&
            Enum.TryParse<ShareScope>(scope, false, out var parsedScope) &&
            parsedScope is ShareScope.FullProfile or
                ShareScope.PreTriage or
                ShareScope.SpecificRecords;
    }
}
