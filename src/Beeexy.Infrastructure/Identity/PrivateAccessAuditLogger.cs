using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Identity;

public sealed class PrivateAccessAuditLogger(
    ILogger<PrivateAccessAuditLogger> logger) : IPrivateAccessAuditLogger
{
    public void LoginFailed(
        string category,
        EntityId? credentialId = null,
        EntityId? accountId = null) =>
        logger.LogWarning(
            "Private access login failed with category {Category}, credential {CredentialId}, account {AccountId}.",
            category,
            credentialId?.Value,
            accountId?.Value);

    public void LoginSucceeded(EntityId credentialId, EntityId accountId) =>
        logger.LogInformation(
            "Private access login succeeded for credential {CredentialId}, account {AccountId}.",
            credentialId.Value,
            accountId.Value);

    public void SessionEnded(EntityId credentialId, EntityId accountId, string reason) =>
        logger.LogInformation(
            "Private access session ended for credential {CredentialId}, account {AccountId}, reason {Reason}.",
            credentialId.Value,
            accountId.Value,
            reason);

    public void CredentialChanged(EntityId credentialId, EntityId accountId, string action) =>
        logger.LogInformation(
            "Private access credential {CredentialId} for account {AccountId} changed: {Action}.",
            credentialId.Value,
            accountId.Value,
            action);
}
