using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public sealed class CreateManagedPatient(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    IManagedPatientCreationRepository repository,
    IIdentityVerificationTransaction transaction,
    ICareRelationshipAuditLogger auditLogger)
{
    public async Task<CreateManagedPatientResult> ExecuteAsync(
        CreateManagedPatientCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        ValidateUnsupportedFields(command);
        var relationshipType = ParseRelationshipType(command.RelationshipType);
        ValidateAttestationAcceptance(command.AttestationAccepted);
        var now = clock.UtcNow;
        var attestation = ParseAttestation(command.AttestationVersion, now);
        var demographics = ParseDemographics(command.Patient, now);

        await transaction.BeginAsync(cancellationToken);

        var subjectProfileId = EntityId.New();
        var subject = PatientProfile.CreateManaged(
            BeeexyId.Create($"BXY-{subjectProfileId.Value:N}".ToUpperInvariant()),
            demographics.FirstName,
            demographics.LastName,
            demographics.DateOfBirth,
            demographics.SexAssignedAtBirth,
            demographics.State,
            now,
            subjectProfileId);
        var relationship = CareRelationship.Create(
            current.PrimaryProfile.Id,
            subject.Id,
            relationshipType,
            current.Account.Id,
            attestation,
            now);

        repository.Add(subject, relationship);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (ManagedPatientCreationConflictException)
        {
            auditLogger.CreationConflict(
                current.Account.Id,
                current.PrimaryProfile.Id,
                relationshipType);
            throw;
        }

        auditLogger.CreationSucceeded(
            current.Account.Id,
            current.PrimaryProfile.Id,
            subject.Id,
            relationship.Id,
            relationship.RelationshipType,
            now);

        return new CreateManagedPatientResult(
            relationship.Id,
            relationship.RelationshipType,
            relationship.Status,
            relationship.Attestation.Version,
            relationship.Attestation.AttestedAt,
            subject.Id,
            subject.BeeexyId.Value,
            subject.FirstName!.Value,
            subject.LastName!.Value,
            subject.DateOfBirth!.Value,
            subject.SexAssignedAtBirth!.Value,
            subject.State!.Code,
            subject.Version);
    }

    private static CareRelationshipType ParseRelationshipType(string? value)
    {
        var candidate = value?.Trim();
        foreach (var relationshipType in Enum.GetValues<CareRelationshipType>())
        {
            if (string.Equals(
                    candidate,
                    relationshipType.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return relationshipType;
            }
        }

        throw new RequestValidationException(
            "care_relationship.invalid_type",
            "A supported relationship type is required.");
    }

    private static void ValidateUnsupportedFields(CreateManagedPatientCommand command)
    {
        if (command.UnsupportedFields.Count > 0)
        {
            throw new RequestValidationException(
                "care_relationship.unsupported_field",
                "The care relationship request contains an unsupported field.");
        }

        if (command.UnsupportedPatientFields.Count > 0)
        {
            throw new RequestValidationException(
                "patient.unsupported_field",
                "The managed patient contains an unsupported field.");
        }
    }

    private static void ValidateAttestationAcceptance(bool accepted)
    {
        if (!accepted)
        {
            throw new RequestValidationException(
                "care_relationship.attestation_required",
                "Explicit attestation acceptance is required.");
        }
    }

    private static AuthorizationAttestation ParseAttestation(
        string? version,
        DateTimeOffset attestedAt)
    {
        try
        {
            return AuthorizationAttestation.Create(version ?? string.Empty, attestedAt);
        }
        catch (ArgumentException)
        {
            throw new RequestValidationException(
                "care_relationship.invalid_attestation_version",
                "A valid attestation version is required.");
        }
    }

    private static ManagedPatientDemographics ParseDemographics(
        ManagedPatientDemographicsCommand? command,
        DateTimeOffset currentTime)
    {
        if (command is null)
        {
            throw new RequestValidationException(
                "patient.demographics_required",
                "Managed patient demographics are required.");
        }

        return new ManagedPatientDemographics(
            PatientDemographicValidation.ParseRequiredName(
                command.FirstName,
                "first_name"),
            PatientDemographicValidation.ParseRequiredName(
                command.LastName,
                "last_name"),
            PatientDemographicValidation.ParseRequiredDateOfBirth(
                command.DateOfBirth,
                currentTime),
            PatientDemographicValidation.ParseRequiredSexAssignedAtBirth(
                command.SexAssignedAtBirth),
            PatientDemographicValidation.ParseRequiredState(command.State));
    }

    private sealed record ManagedPatientDemographics(
        PatientName FirstName,
        PatientName LastName,
        DateOnly DateOfBirth,
        SexAssignedAtBirth SexAssignedAtBirth,
        UsState State);
}

public sealed record CreateManagedPatientCommand(
    string? RelationshipType,
    string? AttestationVersion,
    bool AttestationAccepted,
    ManagedPatientDemographicsCommand? Patient)
{
    public IReadOnlyCollection<string> UnsupportedFields { get; init; } = [];

    public IReadOnlyCollection<string> UnsupportedPatientFields { get; init; } = [];
}

public sealed record ManagedPatientDemographicsCommand(
    string? FirstName,
    string? LastName,
    string? DateOfBirth,
    string? SexAssignedAtBirth,
    string? State);

public sealed record CreateManagedPatientResult(
    EntityId RelationshipId,
    CareRelationshipType RelationshipType,
    CareRelationshipStatus RelationshipStatus,
    string AttestationVersion,
    DateTimeOffset AttestedAt,
    EntityId PatientProfileId,
    string BeeexyId,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    SexAssignedAtBirth SexAssignedAtBirth,
    string State,
    long Version);
