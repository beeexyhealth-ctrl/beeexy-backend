using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Patients;

public sealed class MyCircleAuditLogger(
    ILogger<MyCircleAuditLogger> logger) : IMyCircleAuditLogger
{
    public void DuplicateAccessiblePatientDetected(
        EntityId accountId,
        EntityId managerProfileId,
        EntityId subjectProfileId)
    {
        logger.LogWarning(
            "Duplicate accessible-patient row detected for account {AccountId}, " +
            "manager profile {ManagerProfileId}, and subject profile {SubjectProfileId}.",
            accountId.Value,
            managerProfileId.Value,
            subjectProfileId.Value);
    }

    public void PatientAccessDenied(
        EntityId accountId,
        EntityId managerProfileId,
        EntityId targetProfileId,
        PatientAccessDenialCategory category,
        DateTimeOffset occurredAt)
    {
        logger.LogWarning(
            "Patient management access denied for account {AccountId}, manager profile " +
            "{ManagerProfileId}, target profile {TargetProfileId}, category {DenialCategory}, " +
            "at {OccurredAt}.",
            accountId.Value,
            managerProfileId.Value,
            targetProfileId.Value,
            category,
            occurredAt);
    }
}
