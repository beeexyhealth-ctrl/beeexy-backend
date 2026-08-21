using Beeexy.Application.Common;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Patients;

public sealed class UpdateManagedPatient(AuthorizePatientAccess authorizePatientAccess)
{
    public async Task ExecuteAsync(
        EntityId targetProfileId,
        UpdateManagedPatientCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var authorization = await authorizePatientAccess.ExecuteAsync(
            targetProfileId,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new PatientProfileNotFoundException();
        }

        if (command.RequestedFields.Count > 0)
        {
            throw new RequestValidationException(
                "patient.unsupported_field",
                "The patient update contains an unsupported field.");
        }

        throw new RequestValidationException(
            "patient.no_mutable_fields",
            "No patient profile fields are currently available for update.");
    }
}

public sealed record UpdateManagedPatientCommand(
    IReadOnlyCollection<string> RequestedFields);
