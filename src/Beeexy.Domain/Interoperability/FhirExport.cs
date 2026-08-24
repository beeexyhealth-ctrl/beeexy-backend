using Beeexy.Domain.Common;
using Beeexy.Domain.History;

namespace Beeexy.Domain.Interoperability;

public sealed class FhirExport
{
    private FhirExport()
    {
        FhirVersion = null!;
        MappingVersion = null!;
    }

    private FhirExport(
        EntityId id,
        EntityId patientProfileId,
        EntityId sourceClinicalHistoryEventId,
        FhirExportVersionMetadata versions,
        EntityId idempotencyKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        PatientProfileId = patientProfileId;
        SourceClinicalHistoryEventId = sourceClinicalHistoryEventId;
        FhirVersion = versions.FhirVersion;
        MappingVersion = versions.MappingVersion;
        ProfileCanonical = versions.ProfileCanonical;
        ProfileVersion = versions.ProfileVersion;
        IdempotencyKey = idempotencyKey;
        Status = FhirExportStatus.Pending;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId PatientProfileId { get; private set; }

    public EntityId SourceClinicalHistoryEventId { get; private set; }

    public string FhirVersion { get; private set; }

    public string MappingVersion { get; private set; }

    public string? ProfileCanonical { get; private set; }

    public string? ProfileVersion { get; private set; }

    public FhirExportStatus Status { get; private set; }

    public string? ChecksumAlgorithm { get; private set; }

    public string? Checksum { get; private set; }

    public string? PrivateArtifactStorageUri { get; private set; }

    public EntityId IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? GeneratedAt { get; private set; }

    public DateTimeOffset? ValidationCompletedAt { get; private set; }

    public FhirValidationOutcome? ValidationOutcome { get; private set; }

    public DateTimeOffset? ValidatedAt =>
        Status == FhirExportStatus.Validated ? ValidationCompletedAt : null;

    public FhirExportVersionMetadata Versions => FhirExportVersionMetadata.Create(
        FhirVersion,
        MappingVersion,
        ProfileCanonical,
        ProfileVersion);

    public FhirArtifactMetadata? Artifact => ChecksumAlgorithm is null
        ? null
        : FhirArtifactMetadata.Create(
            ChecksumAlgorithm,
            Checksum!,
            PrivateArtifactStorageUri!);

    public static FhirExport CreatePending(
        ClinicalHistoryEvent source,
        FhirExportVersionMetadata versions,
        EntityId idempotencyKey,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(versions);
        EnsureNonEmpty(idempotencyKey, nameof(idempotencyKey));
        InstantGuard.EnsureNotBefore(createdAt, source.RecordedAt, nameof(createdAt));

        return new FhirExport(
            id ?? EntityId.New(),
            source.PatientProfileId,
            source.Id,
            versions,
            idempotencyKey,
            createdAt);
    }

    public static FhirExport CreatePending(
        ClinicalHistoryEvent source,
        string fhirVersion,
        string mappingVersion,
        EntityId idempotencyKey,
        DateTimeOffset createdAt,
        string? profileCanonical = null,
        string? profileVersion = null,
        EntityId? id = null)
    {
        return CreatePending(
            source,
            FhirExportVersionMetadata.Create(
                fhirVersion,
                mappingVersion,
                profileCanonical,
                profileVersion),
            idempotencyKey,
            createdAt,
            id);
    }

    public void MarkGenerated(
        FhirArtifactMetadata artifact,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        EnsureStatus(FhirExportStatus.Pending);
        InstantGuard.EnsureNotBefore(generatedAt, CreatedAt, nameof(generatedAt));

        ChecksumAlgorithm = artifact.ChecksumAlgorithm;
        Checksum = artifact.Checksum;
        PrivateArtifactStorageUri = artifact.PrivateStorageUri;
        GeneratedAt = generatedAt;
        Status = FhirExportStatus.Generated;
        UpdatedAt = generatedAt;
    }

    public FhirValidationResult RecordValidation(
        FhirValidationOutcome outcome,
        FhirValidatorMetadata validator,
        int errorCount,
        int warningCount,
        DateTimeOffset validationCompletedAt,
        EntityId? validationResultId = null)
    {
        var result = FhirValidationResult.Create(
            this,
            outcome,
            validator,
            errorCount,
            warningCount,
            validationCompletedAt,
            validationResultId);
        ApplyValidationResult(result);
        return result;
    }

    public void ApplyValidationResult(FhirValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureStatus(FhirExportStatus.Generated);

        if (result.FhirExportId != Id)
        {
            throw new ArgumentException(
                "The validation result belongs to a different export.",
                nameof(result));
        }

        if (result.ArtifactChecksumAlgorithm != ChecksumAlgorithm ||
            result.ArtifactChecksum != Checksum)
        {
            throw new ArgumentException(
                "The validation result does not identify this immutable artifact.",
                nameof(result));
        }

        Status = result.Outcome switch
        {
            FhirValidationOutcome.Passed => FhirExportStatus.Validated,
            FhirValidationOutcome.Failed => FhirExportStatus.ValidationFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(result), "Unsupported validation outcome.")
        };
        ValidationOutcome = result.Outcome;
        ValidationCompletedAt = result.ValidatedAt;
        UpdatedAt = result.ValidatedAt;
    }

    private void EnsureStatus(FhirExportStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"The export must be {expected} for this transition.");
        }
    }

    private static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }
}
