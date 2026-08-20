using Beeexy.Domain.Common;

namespace Beeexy.Application.Patients;

public interface IAccountProfileAuditLogger
{
    void InvariantViolation(EntityId accountId, string invariant);

    void ProfileUpdateSucceeded(
        EntityId accountId,
        EntityId profileId,
        IReadOnlyCollection<string> changedFields,
        DateTimeOffset occurredAt);

    void ProfileUpdateConflict(EntityId accountId, EntityId profileId);
}
