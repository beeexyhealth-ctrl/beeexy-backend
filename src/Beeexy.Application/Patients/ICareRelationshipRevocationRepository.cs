using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public interface ICareRelationshipRevocationRepository
{
    Task<CareRelationship?> FindForUpdateAsync(
        EntityId relationshipId,
        EntityId managerProfileId,
        CancellationToken cancellationToken = default);
}
