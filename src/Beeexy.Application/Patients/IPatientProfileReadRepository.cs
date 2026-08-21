using Beeexy.Domain.Common;

namespace Beeexy.Application.Patients;

public interface IPatientProfileReadRepository
{
    Task<PatientProfileReadRecord?> FindAsync(
        EntityId profileId,
        CancellationToken cancellationToken = default);
}

public sealed record PatientProfileReadRecord(
    EntityId ProfileId,
    string BeeexyId);
