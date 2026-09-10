using Beeexy.Application.Interoperability;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Interoperability;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Sharing;

internal sealed class Phase6ValidatedFhirExportProvider(
    BeeexyDbContext dbContext,
    CreateFhirExport createFhirExport,
    IFhirExportReadRepository readRepository,
    IFhirArtifactStore artifactStore,
    FhirArtifactChecksumCalculator checksumCalculator)
    : IPhase6ValidatedFhirExportProvider
{
    public async Task<ValidatedFhirExportArtifact> GenerateAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sourceId = await ResolveSourceAsync(
                patientProfileId,
                idempotencyKey,
                cancellationToken);
            if (sourceId is null)
            {
                throw new Phase6ValidatedFhirExportUnavailableException();
            }

            var created = await createFhirExport.ExecuteAsync(
                new CreateFhirExportCommand(
                    patientProfileId,
                    sourceId.Value,
                    idempotencyKey),
                cancellationToken);
            var state = await readRepository.FindAsync(
                created.Metadata.Id,
                cancellationToken);
            if (state is null ||
                state.Export.Status != FhirExportStatus.Validated ||
                state.Export.Artifact is null ||
                !FhirR4BaseMvp.ValidationSpecification().Matches(state.Export))
            {
                throw new Phase6ValidatedFhirExportUnavailableException();
            }

            var bytes = await artifactStore.ReadAsync(
                FhirArtifactStorageReference.FromPrivateUri(
                    state.Export.Artifact.PrivateStorageUri),
                cancellationToken);
            if (!checksumCalculator.Matches(
                    bytes,
                    state.Export.Artifact.ChecksumAlgorithm,
                    state.Export.Artifact.Checksum))
            {
                throw new Phase6ValidatedFhirExportUnavailableException();
            }

            return new ValidatedFhirExportArtifact(
                state.Export.Id,
                $"fhir-r4-{FhirR4BaseMvp.FhirRelease}/{FhirR4BaseMvp.MappingVersion}",
                FhirR4BaseMvp.MediaType,
                bytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            FhirExportSourceNotFoundException or
            FhirExportIdempotencyConflictException or
            FhirExportValidationRejectedException or
            FhirExportMappingUnavailableException or
            FhirExportInfrastructureUnavailableException or
            FhirExportArtifactIntegrityException or
            FhirExportNotGeneratedException or
            FhirMappingInputException or
            FhirR4BundleSerializationException or
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            throw new Phase6ValidatedFhirExportUnavailableException();
        }
    }

    private async Task<EntityId?> ResolveSourceAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.FhirExports
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.PatientProfileId == patientProfileId &&
                    value.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing is not null)
        {
            return existing.SourceClinicalHistoryEventId;
        }

        return await dbContext.ClinicalHistoryEvents
            .AsNoTracking()
            .Where(value => value.PatientProfileId == patientProfileId &&
                value.EventType == ClinicalHistoryEventType.CompletedPreTriage)
            .OrderByDescending(value => value.OccurredAt)
            .ThenByDescending(value => value.Id)
            .Select(value => (EntityId?)value.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
