using Beeexy.Domain.Common;

namespace Beeexy.Application.Patients;

public interface IPatientProfileAuditLogger
{
    void UpdateSucceeded(
        EntityId actorAccountId,
        EntityId targetProfileId,
        PatientAccessReason accessReason,
        IReadOnlyCollection<string> changedFields,
        DateTimeOffset occurredAt);

    void UpdateConflict(
        EntityId actorAccountId,
        EntityId targetProfileId,
        PatientAccessReason accessReason);
}
