using Beeexy.Domain.Common;

namespace Beeexy.Application.Patients;

public sealed class GetPatientProfile(
    AuthorizePatientAccess authorizePatientAccess,
    IPatientProfileReadRepository repository)
{
    public async Task<GetPatientProfileResult> ExecuteAsync(
        EntityId targetProfileId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await authorizePatientAccess.ExecuteAsync(
            targetProfileId,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new PatientProfileNotFoundException();
        }

        var profile = await repository.FindAsync(targetProfileId, cancellationToken);
        if (profile is null)
        {
            throw new PatientProfileNotFoundException();
        }

        return new GetPatientProfileResult(
            profile.ProfileId,
            profile.BeeexyId,
            profile.FirstName,
            profile.LastName,
            profile.DateOfBirth,
            profile.SexAssignedAtBirth,
            profile.State,
            profile.Version,
            authorization.Reason);
    }
}

public sealed record GetPatientProfileResult(
    EntityId ProfileId,
    string BeeexyId,
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    Beeexy.Domain.Patients.SexAssignedAtBirth? SexAssignedAtBirth,
    string? State,
    long Version,
    PatientAccessReason AuthorizationReason);
