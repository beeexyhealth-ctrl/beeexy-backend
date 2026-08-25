using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Interoperability;

namespace Beeexy.Application.Interoperability;

public sealed record FhirExportReadState(
    FhirExport Export,
    FhirValidationResult? ValidationResult);

public interface IFhirExportReadRepository
{
    Task<FhirExportReadState?> FindAsync(
        EntityId fhirExportId,
        CancellationToken cancellationToken = default);
}

public interface IFhirExportRuntimeVersionProvider
{
    string Version { get; }
}

public interface IFhirExportAuditLogger
{
    void Created(
        EntityId actorAccountId,
        EntityId patientProfileId,
        EntityId fhirExportId,
        PatientAccessReason accessReason,
        DateTimeOffset occurredAt);

    void ValidationCompleted(
        EntityId patientProfileId,
        EntityId fhirExportId,
        FhirExportStatus status,
        DateTimeOffset occurredAt);

    void Downloaded(
        EntityId actorAccountId,
        EntityId patientProfileId,
        EntityId fhirExportId,
        PatientAccessReason accessReason,
        DateTimeOffset occurredAt);

    void IntegrityRejected(
        EntityId actorAccountId,
        EntityId patientProfileId,
        EntityId fhirExportId,
        PatientAccessReason accessReason,
        DateTimeOffset occurredAt);
}

public sealed record FhirExportValidationMetadata(
    FhirValidationOutcome Outcome,
    int ErrorCount,
    int WarningCount,
    DateTimeOffset CompletedAt);

public sealed record FhirExportMetadata(
    EntityId Id,
    FhirExportStatus Status,
    string FhirVersion,
    string MappingVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? GeneratedAt,
    DateTimeOffset? ValidationCompletedAt,
    FhirExportValidationMetadata? Validation);

public sealed record CreateFhirExportCommand(
    EntityId PatientProfileId,
    EntityId SourceClinicalHistoryEventId,
    EntityId IdempotencyKey);

public sealed record CreateFhirExportResult(
    FhirExportMetadata Metadata,
    bool NewlyCreated);

public sealed record DownloadFhirExportResult(
    EntityId FhirExportId,
    byte[] ArtifactBytes,
    string MediaType,
    string FileName);

public sealed class FhirExportNotFoundException : Exception
{
    public FhirExportNotFoundException()
        : base("The FHIR export could not be found.")
    {
    }
}

public sealed class FhirExportDownloadStateConflictException : Exception
{
    public FhirExportDownloadStateConflictException()
        : base("Only a validated current R4 export can be downloaded as FHIR.")
    {
    }
}

public sealed class FhirExportValidationRejectedException : Exception
{
    public FhirExportValidationRejectedException()
        : base("The generated artifact did not pass FHIR validation.")
    {
    }
}

public sealed class FhirExportMappingUnavailableException : Exception
{
    public FhirExportMappingUnavailableException()
        : base("The requested source cannot be exported with the current FHIR mapping.")
    {
    }
}

public sealed class FhirExportInfrastructureUnavailableException : Exception
{
    public FhirExportInfrastructureUnavailableException()
        : base("FHIR export infrastructure is currently unavailable.")
    {
    }
}

public sealed class FhirExportArtifactIntegrityException : Exception
{
    public FhirExportArtifactIntegrityException()
        : base("The immutable FHIR artifact failed its integrity check.")
    {
    }
}

public sealed class CreateFhirExport(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    AuthorizePatientAccess authorizePatientAccess,
    GenerateFhirExport generateFhirExport,
    ValidateFhirExport validateFhirExport,
    IFhirExportRuntimeVersionProvider runtimeVersionProvider,
    IFhirExportAuditLogger auditLogger)
{
    public async Task<CreateFhirExportResult> ExecuteAsync(
        CreateFhirExportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureNonEmpty(command.PatientProfileId, nameof(command.PatientProfileId));
        EnsureNonEmpty(
            command.SourceClinicalHistoryEventId,
            nameof(command.SourceClinicalHistoryEventId));
        EnsureNonEmpty(command.IdempotencyKey, nameof(command.IdempotencyKey));

        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var authorization = await authorizePatientAccess.ExecuteAsync(
            command.PatientProfileId,
            current,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new FhirExportNotFoundException();
        }

        var generated = await generateFhirExport.ExecuteAsync(
            new GenerateFhirExportCommand(
                command.PatientProfileId,
                command.SourceClinicalHistoryEventId,
                command.IdempotencyKey,
                FhirR4BaseMvp.MappingSpecification(),
                runtimeVersionProvider.Version),
            cancellationToken);
        if (generated.NewlyGenerated)
        {
            auditLogger.Created(
                current.Account.Id,
                command.PatientProfileId,
                generated.Export.Id,
                authorization.Reason,
                clock.UtcNow);
        }

        var validation = await validateFhirExport.ExecuteAsync(
            new ValidateFhirExportCommand(
                command.PatientProfileId,
                generated.Export.Id),
            cancellationToken);
        if (validation.NewlyCompleted)
        {
            auditLogger.ValidationCompleted(
                command.PatientProfileId,
                generated.Export.Id,
                validation.Export.Status,
                validation.Export.ValidationCompletedAt!.Value);
        }

        return validation.PipelineStatus switch
        {
            FhirValidationPipelineStatus.Validated => new CreateFhirExportResult(
                FhirExportMetadataFactory.Create(
                    validation.Export,
                    validation.ValidationResult),
                generated.NewlyGenerated),
            FhirValidationPipelineStatus.ValidationFailed =>
                throw new FhirExportValidationRejectedException(),
            FhirValidationPipelineStatus.Blocked or
                FhirValidationPipelineStatus.UnsupportedSpecification =>
                throw new FhirExportMappingUnavailableException(),
            FhirValidationPipelineStatus.IntegrityFailed =>
                throw new FhirExportArtifactIntegrityException(),
            FhirValidationPipelineStatus.ArtifactUnavailable or
                FhirValidationPipelineStatus.ValidatorUnavailable =>
                throw new FhirExportInfrastructureUnavailableException(),
            _ => throw new InvalidOperationException(
                "The FHIR validation pipeline returned an unsupported status.")
        };
    }

    private static void EnsureNonEmpty(EntityId value, string parameterName)
    {
        if (value.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "An entity identifier cannot be empty.",
                parameterName);
        }
    }
}

public sealed class GetFhirExport(
    CurrentAccountProfileResolver currentAccountResolver,
    AuthorizePatientAccess authorizePatientAccess,
    IFhirExportReadRepository repository)
{
    public async Task<FhirExportMetadata> ExecuteAsync(
        EntityId fhirExportId,
        CancellationToken cancellationToken = default)
    {
        var state = await repository.FindAsync(fhirExportId, cancellationToken)
            ?? throw new FhirExportNotFoundException();
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var authorization = await authorizePatientAccess.ExecuteAsync(
            state.Export.PatientProfileId,
            current,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new FhirExportNotFoundException();
        }

        return FhirExportMetadataFactory.Create(
            state.Export,
            state.ValidationResult);
    }
}

public sealed class DownloadFhirExport(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    AuthorizePatientAccess authorizePatientAccess,
    IFhirExportReadRepository repository,
    IFhirArtifactStore artifactStore,
    FhirArtifactChecksumCalculator checksumCalculator,
    IFhirExportAuditLogger auditLogger)
{
    public async Task<DownloadFhirExportResult> ExecuteAsync(
        EntityId fhirExportId,
        CancellationToken cancellationToken = default)
    {
        var state = await repository.FindAsync(fhirExportId, cancellationToken)
            ?? throw new FhirExportNotFoundException();
        var export = state.Export;
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var authorization = await authorizePatientAccess.ExecuteAsync(
            export.PatientProfileId,
            current,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new FhirExportNotFoundException();
        }

        if (export.Status != FhirExportStatus.Validated ||
            export.Artifact is null ||
            !FhirR4BaseMvp.ValidationSpecification().Matches(export))
        {
            throw new FhirExportDownloadStateConflictException();
        }

        byte[] bytes;
        try
        {
            bytes = await artifactStore.ReadAsync(
                FhirArtifactStorageReference.FromPrivateUri(
                    export.Artifact.PrivateStorageUri),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new FhirExportInfrastructureUnavailableException();
        }

        if (!checksumCalculator.Matches(
            bytes,
            export.Artifact.ChecksumAlgorithm,
            export.Artifact.Checksum))
        {
            auditLogger.IntegrityRejected(
                current.Account.Id,
                export.PatientProfileId,
                export.Id,
                authorization.Reason,
                clock.UtcNow);
            throw new FhirExportArtifactIntegrityException();
        }

        auditLogger.Downloaded(
            current.Account.Id,
            export.PatientProfileId,
            export.Id,
            authorization.Reason,
            clock.UtcNow);
        return new DownloadFhirExportResult(
            export.Id,
            bytes,
            FhirR4BaseMvp.MediaType,
            $"beeexy-fhir-export-{export.Id.Value:D}.json");
    }
}

internal static class FhirExportMetadataFactory
{
    public static FhirExportMetadata Create(
        FhirExport export,
        FhirValidationResult? validationResult)
    {
        ArgumentNullException.ThrowIfNull(export);
        if ((export.Status is FhirExportStatus.Validated or
                FhirExportStatus.ValidationFailed) &&
            validationResult is null)
        {
            throw new FhirValidationPersistenceInvariantException();
        }

        return new FhirExportMetadata(
            export.Id,
            export.Status,
            export.FhirVersion,
            export.MappingVersion,
            export.CreatedAt,
            export.GeneratedAt,
            export.ValidationCompletedAt,
            validationResult is null
                ? null
                : new FhirExportValidationMetadata(
                    validationResult.Outcome,
                    validationResult.ErrorCount,
                    validationResult.WarningCount,
                    validationResult.ValidatedAt));
    }
}
