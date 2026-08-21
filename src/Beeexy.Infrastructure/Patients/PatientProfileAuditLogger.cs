using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Patients;

public sealed class PatientProfileAuditLogger(
    ILogger<PatientProfileAuditLogger> logger) : IPatientProfileAuditLogger
{
    public void UpdateSucceeded(
        EntityId actorAccountId,
        EntityId targetProfileId,
        PatientAccessReason accessReason,
        IReadOnlyCollection<string> changedFields,
        DateTimeOffset occurredAt)
    {
        logger.LogInformation(
            "Patient demographic update succeeded for actor account {ActorAccountId}, " +
            "target profile {TargetProfileId}, access reason {AccessReason}, changed fields " +
            "{ChangedFields}, at {OccurredAt}.",
            actorAccountId.Value,
            targetProfileId.Value,
            accessReason,
            string.Join(',', changedFields),
            occurredAt);
    }

    public void UpdateConflict(
        EntityId actorAccountId,
        EntityId targetProfileId,
        PatientAccessReason accessReason)
    {
        logger.LogWarning(
            "Patient demographic update conflict for actor account {ActorAccountId}, " +
            "target profile {TargetProfileId}, access reason {AccessReason}.",
            actorAccountId.Value,
            targetProfileId.Value,
            accessReason);
    }
}
