using Beeexy.Domain.Common;
using Beeexy.Domain.Interoperability;

namespace Beeexy.Application.Interoperability;

public sealed record ValidateFhirExportCommand(
    EntityId PatientProfileId,
    EntityId FhirExportId);

public enum FhirValidationPipelineStatus
{
    Blocked = 1,
    IntegrityFailed = 2,
    ArtifactUnavailable = 3,
    Validated = 4,
    ValidationFailed = 5,
    ValidatorUnavailable = 6,
    UnsupportedSpecification = 7
}

public sealed record ValidateFhirExportResult(
    FhirExport Export,
    FhirValidationPipelineStatus PipelineStatus,
    bool NewlyCompleted,
    FhirValidationResult? ValidationResult,
    FhirValidationEligibility? Eligibility,
    FhirValidationDiagnosticSummary Diagnostics);

public sealed record FhirExportValidationState(
    FhirExport Export,
    FhirValidationResult? ValidationResult);

public interface IFhirExportValidationTransaction : IAsyncDisposable
{
    Task BeginAsync(
        EntityId fhirExportId,
        CancellationToken cancellationToken = default);

    Task<FhirExportValidationState?> LoadAsync(
        EntityId patientProfileId,
        EntityId fhirExportId,
        CancellationToken cancellationToken = default);

    void Add(FhirValidationResult result);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

public sealed class FhirExportForValidationNotFoundException : Exception
{
    public FhirExportForValidationNotFoundException()
        : base("The FHIR export was not found for validation.")
    {
    }
}

public sealed class FhirExportNotGeneratedException : Exception
{
    public FhirExportNotGeneratedException()
        : base("Only a generated FHIR export can enter validation.")
    {
    }
}

public sealed class FhirValidationPersistenceInvariantException : Exception
{
    public FhirValidationPersistenceInvariantException()
        : base("The persisted FHIR validation lifecycle is inconsistent.")
    {
    }
}

public sealed class ValidateFhirExport(
    IClock clock,
    IFhirExportValidationTransaction transaction,
    IFhirArtifactStore artifactStore,
    FhirArtifactChecksumCalculator checksumCalculator,
    IFhirValidationPrerequisiteEvaluator prerequisiteEvaluator,
    IFhirValidator validator,
    FhirValidationDiagnosticSanitizer diagnosticSanitizer)
{
    public async Task<ValidateFhirExportResult> ExecuteAsync(
        ValidateFhirExportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureNonEmpty(command.PatientProfileId, nameof(command.PatientProfileId));
        EnsureNonEmpty(command.FhirExportId, nameof(command.FhirExportId));

        await transaction.BeginAsync(command.FhirExportId, cancellationToken);
        var state = await transaction.LoadAsync(
            command.PatientProfileId,
            command.FhirExportId,
            cancellationToken) ?? throw new FhirExportForValidationNotFoundException();
        var export = state.Export;

        if (export.Status is FhirExportStatus.Validated or
            FhirExportStatus.ValidationFailed)
        {
            if (state.ValidationResult is null)
            {
                throw new FhirValidationPersistenceInvariantException();
            }

            await transaction.CommitAsync(cancellationToken);
            return ExistingFinal(export, state.ValidationResult);
        }

        if (export.Status != FhirExportStatus.Generated ||
            export.Artifact is null)
        {
            throw new FhirExportNotGeneratedException();
        }

        byte[] artifactBytes;
        try
        {
            var reference = FhirArtifactStorageReference.FromPrivateUri(
                export.Artifact.PrivateStorageUri);
            artifactBytes = await artifactStore.ReadAsync(reference, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsArtifactAccessFailure(exception))
        {
            await transaction.CommitAsync(cancellationToken);
            return Transient(
                export,
                FhirValidationPipelineStatus.ArtifactUnavailable);
        }

        if (!checksumCalculator.Matches(
            artifactBytes,
            export.Artifact.ChecksumAlgorithm,
            export.Artifact.Checksum))
        {
            await transaction.CommitAsync(cancellationToken);
            return Transient(export, FhirValidationPipelineStatus.IntegrityFailed);
        }

        var eligibility = prerequisiteEvaluator.Evaluate(export);
        if (!eligibility.IsEligible)
        {
            await transaction.CommitAsync(cancellationToken);
            return new ValidateFhirExportResult(
                export,
                FhirValidationPipelineStatus.Blocked,
                false,
                null,
                eligibility,
                FhirValidationDiagnosticSummary.None);
        }

        var specification = eligibility.Specification!;
        if (!specification.Matches(export))
        {
            await transaction.CommitAsync(cancellationToken);
            return Transient(
                export,
                FhirValidationPipelineStatus.UnsupportedSpecification);
        }

        FhirValidatorExecutionResult execution;
        try
        {
            execution = await validator.ValidateAsync(
                new FhirValidatorRequest(
                    export.Id,
                    artifactBytes,
                    export.Artifact.ChecksumAlgorithm,
                    export.Artifact.Checksum,
                    specification),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await transaction.CommitAsync(cancellationToken);
            return Transient(
                export,
                FhirValidationPipelineStatus.ValidatorUnavailable);
        }

        if (execution.Status == FhirValidatorExecutionStatus.Unavailable)
        {
            await transaction.CommitAsync(cancellationToken);
            return Transient(
                export,
                FhirValidationPipelineStatus.ValidatorUnavailable);
        }

        if (execution.Status == FhirValidatorExecutionStatus.UnsupportedSpecification)
        {
            await transaction.CommitAsync(cancellationToken);
            return Transient(
                export,
                FhirValidationPipelineStatus.UnsupportedSpecification);
        }

        var diagnostics = diagnosticSanitizer.Sanitize(execution.Diagnostics);
        var outcome = execution.Status switch
        {
            FhirValidatorExecutionStatus.Valid => FhirValidationOutcome.Passed,
            FhirValidatorExecutionStatus.Invalid => FhirValidationOutcome.Failed,
            _ => throw new InvalidOperationException(
                "The validator returned an unsupported completed result.")
        };
        var validationResult = export.RecordValidation(
            outcome,
            execution.Validator ?? throw new InvalidOperationException(
                "A completed validator result requires validator identity."),
            diagnostics.ErrorCount,
            diagnostics.WarningCount,
            ToPostgreSqlPrecision(clock.UtcNow));
        transaction.Add(validationResult);
        await transaction.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ValidateFhirExportResult(
            export,
            outcome == FhirValidationOutcome.Passed
                ? FhirValidationPipelineStatus.Validated
                : FhirValidationPipelineStatus.ValidationFailed,
            true,
            validationResult,
            eligibility,
            diagnostics);
    }

    private static ValidateFhirExportResult ExistingFinal(
        FhirExport export,
        FhirValidationResult result) => new(
            export,
            result.Outcome == FhirValidationOutcome.Passed
                ? FhirValidationPipelineStatus.Validated
                : FhirValidationPipelineStatus.ValidationFailed,
            false,
            result,
            null,
            new FhirValidationDiagnosticSummary(
                result.ErrorCount,
                result.WarningCount,
                $"FHIR validation completed with {result.ErrorCount} error(s) and " +
                    $"{result.WarningCount} warning(s).",
                []));

    private static ValidateFhirExportResult Transient(
        FhirExport export,
        FhirValidationPipelineStatus status) => new(
            export,
            status,
            false,
            null,
            null,
            FhirValidationDiagnosticSummary.None);

    private static bool IsArtifactAccessFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException;

    private static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "An entity identifier cannot be empty.",
                parameterName);
        }
    }

    private static DateTimeOffset ToPostgreSqlPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);
}
