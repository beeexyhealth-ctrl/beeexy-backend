using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public interface ICareRelationshipAuditLogger
{
    void CreationSucceeded(
        EntityId creatorAccountId,
        EntityId managerProfileId,
        EntityId subjectProfileId,
        EntityId relationshipId,
        CareRelationshipType relationshipType,
        DateTimeOffset occurredAt);

    void CreationConflict(
        EntityId creatorAccountId,
        EntityId managerProfileId,
        CareRelationshipType relationshipType);

    void RevocationSucceeded(
        EntityId actorAccountId,
        EntityId managerProfileId,
        EntityId subjectProfileId,
        EntityId relationshipId,
        CareRelationshipType relationshipType,
        DateTimeOffset occurredAt);
}
