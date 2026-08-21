using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public sealed class UpdateManagedPatient(
    IClock clock,
    ICurrentSessionIdentity currentSessionIdentity,
    AuthorizePatientAccess authorizePatientAccess,
    IPatientProfileUpdateRepository repository,
    IIdentityVerificationTransaction transaction,
    IPatientProfileAuditLogger auditLogger)
{
    public async Task<UpdateManagedPatientResult> ExecuteAsync(
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

        var now = clock.UtcNow;
        var patch = Validate(command, now);
        var actorAccountId = currentSessionIdentity.GetRequired().AccountId;

        await transaction.BeginAsync(cancellationToken);
        var profile = await repository.FindAsync(targetProfileId, cancellationToken);
        if (profile is null)
        {
            throw new PatientProfileNotFoundException();
        }

        if (profile.Version != command.ExpectedVersion)
        {
            auditLogger.UpdateConflict(
                actorAccountId,
                targetProfileId,
                authorization.Reason);
            throw new ProfileUpdateConcurrencyException();
        }

        var changedFields = profile.UpdateDemographics(
            patch.FirstName,
            patch.LastName,
            patch.DateOfBirth,
            patch.SexAssignedAtBirth,
            patch.State,
            now);

        try
        {
            if (changedFields.Count > 0)
            {
                await repository.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (ProfileUpdateConcurrencyException)
        {
            auditLogger.UpdateConflict(
                actorAccountId,
                targetProfileId,
                authorization.Reason);
            throw;
        }

        auditLogger.UpdateSucceeded(
            actorAccountId,
            targetProfileId,
            authorization.Reason,
            changedFields,
            now);

        return ToResult(profile, authorization.Reason);
    }

    internal static UpdateManagedPatientResult ToResult(
        PatientProfile profile,
        PatientAccessReason authorizationReason) =>
        new(
            profile.Id,
            profile.BeeexyId.Value,
            profile.FirstName?.Value,
            profile.LastName?.Value,
            profile.DateOfBirth,
            profile.SexAssignedAtBirth,
            profile.State?.Code,
            profile.Version,
            authorizationReason);

    private static ValidatedPatientPatch Validate(
        UpdateManagedPatientCommand command,
        DateTimeOffset currentTime)
    {
        if (command.UnsupportedFields.Count > 0)
        {
            throw new RequestValidationException(
                "patient.unsupported_field",
                "The patient update contains an unsupported field.");
        }

        if (command.ExpectedVersion is null or <= 0)
        {
            throw new RequestValidationException(
                "patient.invalid_version",
                "A positive patient profile version is required.");
        }

        if (!command.FirstName.IsSpecified &&
            !command.LastName.IsSpecified &&
            !command.DateOfBirth.IsSpecified &&
            !command.SexAssignedAtBirth.IsSpecified &&
            !command.State.IsSpecified)
        {
            throw new RequestValidationException(
                "patient.no_demographic_fields",
                "At least one approved patient demographic field is required.");
        }

        return new ValidatedPatientPatch(
            command.FirstName.IsSpecified
                ? PatientDemographicValidation.ParseRequiredName(
                    command.FirstName.Value,
                    "first_name")
                : null,
            command.LastName.IsSpecified
                ? PatientDemographicValidation.ParseRequiredName(
                    command.LastName.Value,
                    "last_name")
                : null,
            command.DateOfBirth.IsSpecified
                ? PatientDemographicValidation.ParseRequiredDateOfBirth(
                    command.DateOfBirth.Value,
                    currentTime)
                : null,
            command.SexAssignedAtBirth.IsSpecified
                ? PatientDemographicValidation.ParseRequiredSexAssignedAtBirth(
                    command.SexAssignedAtBirth.Value)
                : null,
            command.State.IsSpecified
                ? PatientDemographicValidation.ParseRequiredState(command.State.Value)
                : null);
    }

    private sealed record ValidatedPatientPatch(
        PatientName? FirstName,
        PatientName? LastName,
        DateOnly? DateOfBirth,
        SexAssignedAtBirth? SexAssignedAtBirth,
        UsState? State);
}

public sealed record PatientPatchField<T>(bool IsSpecified, T? Value);

public sealed record UpdateManagedPatientCommand(
    long? ExpectedVersion,
    PatientPatchField<string> FirstName,
    PatientPatchField<string> LastName,
    PatientPatchField<string> DateOfBirth,
    PatientPatchField<string> SexAssignedAtBirth,
    PatientPatchField<string> State,
    IReadOnlyCollection<string> UnsupportedFields);

public sealed record UpdateManagedPatientResult(
    EntityId ProfileId,
    string BeeexyId,
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    SexAssignedAtBirth? SexAssignedAtBirth,
    string? State,
    long Version,
    PatientAccessReason AuthorizationReason);
