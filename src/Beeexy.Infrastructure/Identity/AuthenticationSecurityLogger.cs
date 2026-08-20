using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Identity;

public sealed class AuthenticationSecurityLogger(
    ILogger<AuthenticationSecurityLogger> logger) : IAuthenticationSecurityLogger
{
    public void RefreshSessionRotated(EntityId accountId, EntityId familyId)
    {
        logger.LogInformation(
            "Refresh session rotated for account {AccountId}, family {FamilyId}.",
            accountId.Value,
            familyId.Value);
    }

    public void RefreshReuseDetected(EntityId accountId, EntityId familyId)
    {
        logger.LogWarning(
            "Refresh-token reuse detected for account {AccountId}, family {FamilyId}; family revoked.",
            accountId.Value,
            familyId.Value);
    }

    public void SessionFamilyRevoked(EntityId accountId, EntityId familyId, string reason)
    {
        logger.LogInformation(
            "Refresh-session family revoked for account {AccountId}, family {FamilyId}, reason {Reason}.",
            accountId.Value,
            familyId.Value,
            reason);
    }
}
