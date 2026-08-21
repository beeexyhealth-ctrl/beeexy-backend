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
}
