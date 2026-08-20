using Beeexy.Domain.Common;

namespace Beeexy.Application.Identity;

public interface IAuthenticationSecurityLogger
{
    void RefreshSessionRotated(EntityId accountId, EntityId familyId);

    void RefreshReuseDetected(EntityId accountId, EntityId familyId);

    void SessionFamilyRevoked(EntityId accountId, EntityId familyId, string reason);
}
