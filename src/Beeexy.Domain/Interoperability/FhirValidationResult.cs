using Beeexy.Domain.Common;

namespace Beeexy.Domain.Interoperability;

public sealed class FhirValidationResult
{
    private FhirValidationResult()
    {
        ValidatorName = null!;
        ValidatorVersion = null!;
        ArtifactChecksumAlgorithm = null!;
        ArtifactChecksum = null!;
    }

    private FhirValidationResult(
        EntityId id,
        EntityId fhirExportId,
        FhirValidationOutcome outcome,
        FhirValidatorMetadata validator,
        string artifactChecksumAlgorithm,
        string artifactChecksum,
        int errorCount,
        int warningCount,
        DateTimeOffset validatedAt)
    {
        Id = id;
        FhirExportId = fhirExportId;
        Outcome = outcome;
        ValidatorName = validator.Name;
        ValidatorVersion = validator.Version;
        ArtifactChecksumAlgorithm = artifactChecksumAlgorithm;
        ArtifactChecksum = artifactChecksum;
        ErrorCount = errorCount;
        WarningCount = warningCount;
        ValidatedAt = validatedAt;
    }

    public EntityId Id { get; private set; }

    public EntityId FhirExportId { get; private set; }

    public FhirValidationOutcome Outcome { get; private set; }

    public bool IsValid => Outcome == FhirValidationOutcome.Passed;

    public string ValidatorName { get; private set; }

    public string ValidatorVersion { get; private set; }

    public string ArtifactChecksumAlgorithm { get; private set; }

    public string ArtifactChecksum { get; private set; }

    public int ErrorCount { get; private set; }

    public int WarningCount { get; private set; }

    public DateTimeOffset ValidatedAt { get; private set; }

    public FhirValidatorMetadata Validator =>
        FhirValidatorMetadata.Create(ValidatorName, ValidatorVersion);

    public static FhirValidationResult Create(
        FhirExport export,
        FhirValidationOutcome outcome,
        FhirValidatorMetadata validator,
        int errorCount,
        int warningCount,
        DateTimeOffset validatedAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(validator);

        if (export.Status != FhirExportStatus.Generated ||
            export.GeneratedAt is null ||
            export.Artifact is null)
        {
            throw new InvalidOperationException(
                "Only a generated immutable artifact can be validated.");
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (errorCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(errorCount));
        }

        if (warningCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warningCount));
        }

        if ((outcome == FhirValidationOutcome.Passed && errorCount != 0) ||
            (outcome == FhirValidationOutcome.Failed && errorCount == 0))
        {
            throw new ArgumentException(
                "A passing result cannot contain errors and a failing result must contain at least one error.",
                nameof(errorCount));
        }

        InstantGuard.EnsureNotBefore(
            validatedAt,
            export.GeneratedAt.Value,
            nameof(validatedAt));

        return new FhirValidationResult(
            id ?? EntityId.New(),
            export.Id,
            outcome,
            validator,
            export.Artifact.ChecksumAlgorithm,
            export.Artifact.Checksum,
            errorCount,
            warningCount,
            validatedAt);
    }
}
