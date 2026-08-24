using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Interoperability;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Interoperability;

public sealed record GenerateFhirExportCommand(
    EntityId PatientProfileId,
    EntityId SourceClinicalHistoryEventId,
    EntityId IdempotencyKey,
    FhirMappingSpecificationIdentity MappingSpecification,
    string SoftwareRuntimeVersion);

public sealed record GenerateFhirExportResult(
    FhirExport Export,
    bool NewlyGenerated,
    string ArtifactKind,
    string ArtifactMediaType);

public sealed record FhirExportAuthoritativeSource(
    ClinicalHistoryEvent HistoryEvent,
    PreTriageEpisode Episode,
    ClinicalAssessment Assessment,
    QuestionnaireDefinitionVersion Questionnaire);

public interface IFhirExportGenerationTransaction : IAsyncDisposable
{
    Task BeginAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<FhirExportAuthoritativeSource?> LoadAuthoritativeSourceAsync(
        EntityId patientProfileId,
        EntityId sourceClinicalHistoryEventId,
        CancellationToken cancellationToken = default);

    Task<FhirExport?> FindByIdempotencyKeyAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default);

    void Add(FhirExport export);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

public sealed class FhirExportSourceNotFoundException : Exception
{
    public FhirExportSourceNotFoundException()
        : base("The authoritative FHIR export source was not found.")
    {
    }
}

public sealed class FhirExportIdempotencyConflictException : Exception
{
    public FhirExportIdempotencyConflictException()
        : base("The FHIR export idempotency key belongs to different generation inputs.")
    {
    }
}

public sealed class FhirArtifactReconciliationRequiredException : Exception
{
    public FhirArtifactReconciliationRequiredException(
        Exception generationFailure,
        Exception cleanupFailure)
        : base(
            "FHIR export generation failed and private artifact cleanup requires reconciliation.",
            new AggregateException(generationFailure, cleanupFailure))
    {
    }
}

public sealed class GenerateFhirExport(
    IClock clock,
    IFhirExportGenerationTransaction transaction,
    IFhirArtifactStore artifactStore,
    FhirSnapshotSerializer serializer,
    FhirArtifactChecksumCalculator checksumCalculator)
{
    public async Task<GenerateFhirExportResult> ExecuteAsync(
        GenerateFhirExportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);
        var generatedAt = ToPostgreSqlPrecision(clock.UtcNow);
        FhirArtifactStorageReference? storedReference = null;

        try
        {
            await transaction.BeginAsync(
                command.PatientProfileId,
                command.IdempotencyKey,
                cancellationToken);
            var source = await transaction.LoadAuthoritativeSourceAsync(
                command.PatientProfileId,
                command.SourceClinicalHistoryEventId,
                cancellationToken) ?? throw new FhirExportSourceNotFoundException();
            var existing = await transaction.FindByIdempotencyKeyAsync(
                command.PatientProfileId,
                command.IdempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                EnsureSameGenerationInputs(existing, command);
                await transaction.CommitAsync(cancellationToken);
                return Result(existing, newlyGenerated: false);
            }

            var export = FhirExport.CreatePending(
                source.HistoryEvent,
                FhirExportVersionMetadata.Create(
                    FhirSnapshotArtifactFormat.UnresolvedFhirReleaseMarker,
                    command.MappingSpecification.MappingVersion),
                command.IdempotencyKey,
                generatedAt);
            transaction.Add(export);
            await transaction.SaveChangesAsync(cancellationToken);

            var trace = CreateGenerationTrace(export.Id, generatedAt);
            var snapshot = new FhirSnapshotAssembler(command.MappingSpecification).Assemble(
                new FhirSnapshotAssemblyInput(
                    QuestionnaireResponseMappingInput.Create(
                        source.HistoryEvent,
                        source.Episode,
                        source.Questionnaire),
                    RiskAssessmentMappingInput.Create(
                        source.HistoryEvent,
                        source.Episode,
                        source.Assessment),
                    DeviceMappingInput.Create(command.SoftwareRuntimeVersion),
                    ProvenanceMappingInput.Create(
                        source.HistoryEvent,
                        source.Episode,
                        source.Assessment,
                        trace)));
            var artifactBytes = serializer.Serialize(snapshot);
            var checksum = checksumCalculator.Calculate(artifactBytes);
            var artifactReference = FhirArtifactStorageReference.CreateNew();
            await artifactStore.StoreImmutableAsync(
                artifactReference,
                artifactBytes,
                cancellationToken);
            storedReference = artifactReference;
            export.MarkGenerated(
                FhirArtifactMetadata.Create(
                    FhirArtifactChecksumCalculator.Algorithm,
                    checksum,
                    artifactReference.PrivateUri),
                generatedAt);
            await transaction.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result(export, newlyGenerated: true);
        }
        catch (Exception generationFailure)
        {
            if (storedReference is not null)
            {
                try
                {
                    var deleted = await artifactStore.DeleteAsync(
                        storedReference,
                        CancellationToken.None);
                    if (!deleted)
                    {
                        throw new InvalidOperationException(
                            "The stored private artifact could not be found for cleanup.");
                    }
                }
                catch (Exception cleanupFailure)
                {
                    throw new FhirArtifactReconciliationRequiredException(
                        generationFailure,
                        cleanupFailure);
                }
            }

            throw;
        }
    }

    private static GenerateFhirExportResult Result(
        FhirExport export,
        bool newlyGenerated) => new(
            export,
            newlyGenerated,
            FhirSnapshotArtifactFormat.ArtifactKind,
            FhirSnapshotArtifactFormat.MediaType);

    private static void Validate(GenerateFhirExportCommand command)
    {
        ArgumentNullException.ThrowIfNull(command.MappingSpecification);
        EnsureNonEmpty(command.PatientProfileId, nameof(command.PatientProfileId));
        EnsureNonEmpty(
            command.SourceClinicalHistoryEventId,
            nameof(command.SourceClinicalHistoryEventId));
        EnsureNonEmpty(command.IdempotencyKey, nameof(command.IdempotencyKey));
        _ = DeviceMappingInput.Create(command.SoftwareRuntimeVersion);
    }

    private static void EnsureSameGenerationInputs(
        FhirExport existing,
        GenerateFhirExportCommand command)
    {
        if (existing.SourceClinicalHistoryEventId !=
                command.SourceClinicalHistoryEventId ||
            !string.Equals(
                existing.MappingVersion,
                command.MappingSpecification.MappingVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                existing.FhirVersion,
                FhirSnapshotArtifactFormat.UnresolvedFhirReleaseMarker,
                StringComparison.Ordinal) ||
            existing.ProfileCanonical is not null ||
            existing.ProfileVersion is not null)
        {
            throw new FhirExportIdempotencyConflictException();
        }
    }

    private static FhirGenerationTrace CreateGenerationTrace(
        EntityId exportId,
        DateTimeOffset recordedAt)
    {
        var suffix = exportId.Value.ToString("D");
        return FhirGenerationTrace.Create(
            exportId,
            FhirLogicalResourceIdentity.Create(
                FhirConceptualResource.QuestionnaireResponse,
                $"internal-questionnaire-response:{suffix}"),
            FhirLogicalResourceIdentity.Create(
                FhirConceptualResource.RiskAssessment,
                $"internal-risk-assessment:{suffix}"),
            FhirLogicalResourceIdentity.Create(
                FhirConceptualResource.Device,
                $"internal-device:{suffix}"),
            FhirLogicalResourceIdentity.Create(
                FhirConceptualResource.Provenance,
                $"internal-provenance:{suffix}"),
            recordedAt);
    }

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
