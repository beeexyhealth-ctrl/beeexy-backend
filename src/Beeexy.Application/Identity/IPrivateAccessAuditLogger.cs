using Beeexy.Domain.Common;

namespace Beeexy.Application.Identity;

public interface IPrivateAccessAuditLogger
{
    void LoginFailed(string category, EntityId? credentialId = null, EntityId? accountId = null);
    void LoginSucceeded(EntityId credentialId, EntityId accountId);
    void SessionEnded(EntityId credentialId, EntityId accountId, string reason);
    void CredentialChanged(EntityId credentialId, EntityId accountId, string action);
}
