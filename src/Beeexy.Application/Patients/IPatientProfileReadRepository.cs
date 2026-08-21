using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public interface IPatientProfileReadRepository
{
    Task<PatientProfileReadRecord?> FindAsync(
        EntityId profileId,
        CancellationToken cancellationToken = default);
}

public sealed record PatientProfileReadRecord(
    EntityId ProfileId,
    string BeeexyId,
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    SexAssignedAtBirth? SexAssignedAtBirth,
    string? State,
    long Version);
