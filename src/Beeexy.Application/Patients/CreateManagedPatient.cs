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
        var relationshipType = ParseRelationshipType(command.RelationshipType);
        ValidateAttestationAcceptance(command.AttestationAccepted);
        var now = clock.UtcNow;
        var attestation = ParseAttestation(command.AttestationVersion, now);

        await transaction.BeginAsync(cancellationToken);
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);

        var subjectProfileId = EntityId.New();
        var subject = PatientProfile.Create(
            BeeexyId.Create($"BXY-{subjectProfileId.Value:N}".ToUpperInvariant()),
            now,
            accountId: null,
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
            subject.BeeexyId.Value);
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
}

public sealed record CreateManagedPatientCommand(
    string? RelationshipType,
    string? AttestationVersion,
    bool AttestationAccepted);

public sealed record CreateManagedPatientResult(
    EntityId RelationshipId,
    CareRelationshipType RelationshipType,
    CareRelationshipStatus RelationshipStatus,
    string AttestationVersion,
    DateTimeOffset AttestedAt,
    EntityId PatientProfileId,
    string BeeexyId);
