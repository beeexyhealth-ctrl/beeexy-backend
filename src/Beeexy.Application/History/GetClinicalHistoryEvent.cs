using Beeexy.Application.Patients;
using Beeexy.Domain.Common;

namespace Beeexy.Application.History;

public sealed class GetClinicalHistoryEvent(
    AuthorizePatientAccess authorizePatientAccess,
    IClinicalHistoryEventReadRepository repository)
{
    public async Task<ClinicalHistoryEventDetail> ExecuteAsync(
        EntityId patientProfileId,
        EntityId eventId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await authorizePatientAccess.ExecuteAsync(
            patientProfileId,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new PatientProfileNotFoundException();
        }

        var detail = await repository.GetAsync(
            patientProfileId,
            eventId,
            cancellationToken);
        return detail ?? throw new PatientProfileNotFoundException();
    }
}
