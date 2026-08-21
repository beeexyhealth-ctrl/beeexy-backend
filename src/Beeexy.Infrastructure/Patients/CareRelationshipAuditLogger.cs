using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Patients;

public sealed class CareRelationshipAuditLogger(
    ILogger<CareRelationshipAuditLogger> logger) : ICareRelationshipAuditLogger
{
    public void CreationSucceeded(
        EntityId creatorAccountId,
        EntityId managerProfileId,
        EntityId subjectProfileId,
        EntityId relationshipId,
        CareRelationshipType relationshipType,
        DateTimeOffset occurredAt)
    {
        logger.LogInformation(
            "Care relationship creation succeeded for creator account {CreatorAccountId}, " +
            "manager profile {ManagerProfileId}, subject profile {SubjectProfileId}, " +
            "relationship {RelationshipId}, type {RelationshipType}, at {OccurredAt}.",
            creatorAccountId.Value,
            managerProfileId.Value,
            subjectProfileId.Value,
            relationshipId.Value,
            relationshipType,
            occurredAt);
    }

    public void CreationConflict(
        EntityId creatorAccountId,
        EntityId managerProfileId,
        CareRelationshipType relationshipType)
    {
        logger.LogWarning(
            "Care relationship creation conflict for creator account {CreatorAccountId}, " +
            "manager profile {ManagerProfileId}, type {RelationshipType}.",
            creatorAccountId.Value,
            managerProfileId.Value,
            relationshipType);
    }

    public void RevocationSucceeded(
        EntityId actorAccountId,
        EntityId managerProfileId,
        EntityId subjectProfileId,
        EntityId relationshipId,
        CareRelationshipType relationshipType,
        DateTimeOffset occurredAt)
    {
        logger.LogInformation(
            "Care relationship revocation succeeded for actor account {ActorAccountId}, " +
            "manager profile {ManagerProfileId}, subject profile {SubjectProfileId}, " +
            "relationship {RelationshipId}, type {RelationshipType}, at {OccurredAt}.",
            actorAccountId.Value,
            managerProfileId.Value,
            subjectProfileId.Value,
            relationshipId.Value,
            relationshipType,
            occurredAt);
    }
}
