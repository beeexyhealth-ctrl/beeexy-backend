using Beeexy.Domain.Common;

namespace Beeexy.Application.Patients;

public interface IPatientAccessAuthorizationRepository
{
    Task<PatientAccessAuthorizationLookup> FindAsync(
        EntityId managerProfileId,
        EntityId targetProfileId,
        CancellationToken cancellationToken = default);
}

public sealed record PatientAccessAuthorizationLookup(
    bool TargetExists,
    EntityId? ActiveRelationshipId);
