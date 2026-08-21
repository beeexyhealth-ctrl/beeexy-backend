using Beeexy.Domain.Common;

namespace Beeexy.Application.Patients;

public interface IMyCircleAuditLogger
{
    void DuplicateAccessiblePatientDetected(
        EntityId accountId,
        EntityId managerProfileId,
        EntityId subjectProfileId);

    void PatientAccessDenied(
        EntityId accountId,
        EntityId managerProfileId,
        EntityId targetProfileId,
        PatientAccessDenialCategory category,
        DateTimeOffset occurredAt);
}
