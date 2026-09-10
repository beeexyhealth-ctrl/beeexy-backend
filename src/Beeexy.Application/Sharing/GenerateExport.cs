using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;

namespace Beeexy.Application.Sharing;

public sealed record GenerateExportCommand(
    EntityId PatientProfileId,
    ExportArtifactFormat Format,
    EntityId IdempotencyKey);

public sealed record GenerateExportResult(
    ExportArtifact Artifact,
    bool NewlyCreated);

public sealed record PrivateArtifactStorageReference(
    string OpaqueKey,
    string PrivateStorageIdentity);

public interface IPrivateArtifactStorage
{
    PrivateArtifactStorageReference CreateReference();

    Task StoreImmutableAsync(
        PrivateArtifactStorageReference reference,
        ReadOnlyMemory<byte> artifactBytes,
        CancellationToken cancellationToken = default);

    Task<byte[]> ReadAsync(
        string privateStorageIdentity,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        PrivateArtifactStorageReference reference,
        CancellationToken cancellationToken = default);
}

public interface IPdfExportRenderer
{
    byte[] Render(
        CanonicalSharedHealthSnapshot snapshot,
        EntityId snapshotId,
        DateTimeOffset generatedAt);
}

public static class PdfExportContract
{
    public const string MediaType = "application/pdf";
    public const string FormatVersion = "1.0";
}

public sealed record ValidatedFhirExportArtifact(
    EntityId SnapshotId,
    string SnapshotVersion,
    string MediaType,
    byte[] ArtifactBytes);

public interface IPhase6ValidatedFhirExportProvider
{
    Task<ValidatedFhirExportArtifact> GenerateAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default);
}

public interface IExportArtifactTransaction
{
    Task BeginAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<ExportArtifact?> FindExistingAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default);

    void Add(ExportArtifact artifact);

    Task SaveAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public sealed class ExportGenerationOptions
{
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);
    public const int DefaultMaximumBeeexyJsonBytes = 25 * 1024 * 1024;

    public ExportGenerationOptions(
        TimeSpan retention,
        int maximumBeeexyJsonBytes = DefaultMaximumBeeexyJsonBytes)
    {
        if (retention <= TimeSpan.Zero || retention > TimeSpan.FromDays(3650))
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }

        if (maximumBeeexyJsonBytes <= 0 || maximumBeeexyJsonBytes > 100 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBeeexyJsonBytes));
        }

        Retention = retention;
        MaximumBeeexyJsonBytes = maximumBeeexyJsonBytes;
    }

    public TimeSpan Retention { get; }

    public int MaximumBeeexyJsonBytes { get; }

    public int MaximumArtifactBytes => MaximumBeeexyJsonBytes;
}

public sealed class GenerateExport(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    ICanonicalSharedHealthSnapshotBuilder snapshotBuilder,
    BeeexyJsonExportRenderer beeexyJsonRenderer,
    IPdfExportRenderer pdfRenderer,
    IPhase6ValidatedFhirExportProvider fhirExportProvider,
    ExportArtifactChecksumCalculator checksumCalculator,
    IPrivateArtifactStorage storage,
    IExportArtifactTransaction transaction,
    ExportGenerationOptions options)
{
    public async Task<GenerateExportResult> ExecuteAsync(
        GenerateExportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        if (command.PatientProfileId != current.PrimaryProfile.Id)
        {
            throw new ExportPatientNotFoundException();
        }

        Validate(command);
        if (command.Format == ExportArtifactFormat.FhirJson)
        {
            return await ExecuteFhirAsync(command, current.Account.Id, cancellationToken);
        }

        PrivateArtifactStorageReference? reference = null;
        var storageAttempted = false;
        var storedSuccessfully = false;
        var committed = false;
        try
        {
            await transaction.BeginAsync(
                command.PatientProfileId,
                command.IdempotencyKey,
                cancellationToken);
            var existing = await transaction.FindExistingAsync(
                command.PatientProfileId,
                command.IdempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                if (existing.Format != command.Format)
                {
                    throw new ExportIdempotencyConflictException();
                }

                if (existing.Status != ExportArtifactStatus.Available)
                {
                    throw new ExportArtifactStateConflictException();
                }

                await transaction.CommitAsync(cancellationToken);
                committed = true;
                return new GenerateExportResult(existing, NewlyCreated: false);
            }

            var createdAt = CurrentInstant();
            var snapshotId = EntityId.New();
            CanonicalSharedHealthSnapshot snapshot;
            try
            {
                snapshot = await snapshotBuilder.BuildAsync(
                    command.PatientProfileId,
                    new SharedProfileSelection(ShareScope.FullProfile, true, []),
                    cancellationToken);
            }
            catch (SharedProfileSourceUnavailableException)
            {
                throw new ExportGenerationUnavailableException();
            }

            byte[] artifactBytes;
            string mediaType;
            try
            {
                (artifactBytes, mediaType) = command.Format switch
                {
                    ExportArtifactFormat.BeeexyJson =>
                        (beeexyJsonRenderer.Render(snapshot, snapshotId, createdAt),
                            BeeexyJsonExportRenderer.MediaType),
                    ExportArtifactFormat.Pdf =>
                        (pdfRenderer.Render(snapshot, snapshotId, createdAt),
                            PdfExportContract.MediaType),
                    _ => throw new ExportFormatUnavailableException()
                };
            }
            catch (PdfExportRenderException)
            {
                throw new ExportGenerationUnavailableException();
            }

            if (artifactBytes.Length == 0 ||
                artifactBytes.Length > options.MaximumArtifactBytes)
            {
                throw new ExportGenerationUnavailableException();
            }

            var artifact = ExportArtifact.CreatePending(
                command.PatientProfileId,
                current.Account.Id,
                command.IdempotencyKey,
                command.Format,
                mediaType,
                snapshotId,
                BeeexyJsonExportRenderer.SnapshotVersion,
                createdAt,
                createdAt.Add(options.Retention));
            transaction.Add(artifact);
            await transaction.SaveAsync(cancellationToken);

            reference = storage.CreateReference();
            storageAttempted = true;
            await storage.StoreImmutableAsync(reference, artifactBytes, cancellationToken);
            storedSuccessfully = true;
            artifact.MarkAvailable(
                ExportArtifactContentMetadata.Create(
                    ExportArtifactChecksumCalculator.Algorithm,
                    checksumCalculator.Calculate(artifactBytes),
                    reference.PrivateStorageIdentity),
                createdAt);
            await transaction.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return new GenerateExportResult(artifact, NewlyCreated: true);
        }
        catch (Exception generationFailure)
        {
            await TryRollbackAsync();
            if (!committed && storageAttempted && reference is not null)
            {
                try
                {
                    var deleted = await storage.DeleteAsync(reference, CancellationToken.None);
                    if (storedSuccessfully && !deleted)
                    {
                        throw new InvalidOperationException(
                            "The stored private artifact could not be found for cleanup.");
                    }
                }
                catch (Exception cleanupFailure)
                {
                    throw new ExportArtifactReconciliationRequiredException(
                        generationFailure,
                        cleanupFailure);
                }
            }

            throw;
        }
    }

    private async Task<GenerateExportResult> ExecuteFhirAsync(
        GenerateExportCommand command,
        EntityId requestedByAccountId,
        CancellationToken cancellationToken)
    {
        ExportArtifact? existing;
        try
        {
            await transaction.BeginAsync(
                command.PatientProfileId,
                command.IdempotencyKey,
                cancellationToken);
            existing = await transaction.FindExistingAsync(
                command.PatientProfileId,
                command.IdempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                return await CompleteReplayAsync(existing, command.Format, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await TryRollbackAsync();
            throw;
        }

        ValidatedFhirExportArtifact validated;
        try
        {
            validated = await fhirExportProvider.GenerateAsync(
                command.PatientProfileId,
                command.IdempotencyKey,
                cancellationToken);
        }
        catch (Phase6ValidatedFhirExportUnavailableException)
        {
            throw new ExportGenerationUnavailableException();
        }

        if (validated.ArtifactBytes.Length == 0 ||
            validated.ArtifactBytes.Length > options.MaximumArtifactBytes)
        {
            throw new ExportGenerationUnavailableException();
        }

        PrivateArtifactStorageReference? reference = null;
        var storageAttempted = false;
        var storedSuccessfully = false;
        try
        {
            await transaction.BeginAsync(
                command.PatientProfileId,
                command.IdempotencyKey,
                cancellationToken);
            existing = await transaction.FindExistingAsync(
                command.PatientProfileId,
                command.IdempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                return await CompleteReplayAsync(existing, command.Format, cancellationToken);
            }

            var createdAt = CurrentInstant();
            var artifact = ExportArtifact.CreatePending(
                command.PatientProfileId,
                requestedByAccountId,
                command.IdempotencyKey,
                command.Format,
                validated.MediaType,
                validated.SnapshotId,
                validated.SnapshotVersion,
                createdAt,
                createdAt.Add(options.Retention));
            transaction.Add(artifact);
            await transaction.SaveAsync(cancellationToken);

            reference = storage.CreateReference();
            storageAttempted = true;
            await storage.StoreImmutableAsync(
                reference,
                validated.ArtifactBytes,
                cancellationToken);
            storedSuccessfully = true;
            artifact.MarkAvailable(
                ExportArtifactContentMetadata.Create(
                    ExportArtifactChecksumCalculator.Algorithm,
                    checksumCalculator.Calculate(validated.ArtifactBytes),
                    reference.PrivateStorageIdentity),
                createdAt);
            await transaction.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new GenerateExportResult(artifact, NewlyCreated: true);
        }
        catch (Exception generationFailure)
        {
            await TryRollbackAsync();
            if (storageAttempted && reference is not null)
            {
                try
                {
                    var deleted = await storage.DeleteAsync(reference, CancellationToken.None);
                    if (storedSuccessfully && !deleted)
                    {
                        throw new InvalidOperationException(
                            "The stored private artifact could not be found for cleanup.");
                    }
                }
                catch (Exception cleanupFailure)
                {
                    throw new ExportArtifactReconciliationRequiredException(
                        generationFailure,
                        cleanupFailure);
                }
            }

            throw;
        }
    }

    private async Task<GenerateExportResult> CompleteReplayAsync(
        ExportArtifact existing,
        ExportArtifactFormat requestedFormat,
        CancellationToken cancellationToken)
    {
        if (existing.Format != requestedFormat)
        {
            throw new ExportIdempotencyConflictException();
        }

        if (existing.Status != ExportArtifactStatus.Available)
        {
            throw new ExportArtifactStateConflictException();
        }

        await transaction.CommitAsync(cancellationToken);
        return new GenerateExportResult(existing, NewlyCreated: false);
    }

    private static void Validate(GenerateExportCommand command)
    {
        if (command.PatientProfileId.Value == Guid.Empty ||
            command.IdempotencyKey.Value == Guid.Empty)
        {
            throw new RequestValidationException(
                "sharing.export_identifiers_required",
                "A patient and non-empty idempotency key are required.");
        }

        if (!Enum.IsDefined(command.Format))
        {
            throw new RequestValidationException(
                "sharing.export_format_invalid",
                "The export format is invalid.");
        }

    }

    private async Task TryRollbackAsync()
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // The original failure remains authoritative. Storage compensation still runs.
        }
    }

    private DateTimeOffset CurrentInstant()
    {
        var utc = clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}

public sealed class ExportPatientNotFoundException : Exception;

public sealed class ExportFormatUnavailableException : Exception;

public sealed class ExportGenerationUnavailableException : Exception;

public sealed class ExportIdempotencyConflictException : Exception;

public sealed class ExportArtifactStateConflictException : Exception;

public sealed class ExportArtifactReconciliationRequiredException(
    Exception generationFailure,
    Exception cleanupFailure)
    : Exception(
        "Export generation failed and private artifact cleanup requires reconciliation.",
        new AggregateException(generationFailure, cleanupFailure));

public sealed class PdfExportRenderException(Exception innerException)
    : Exception("The PDF export could not be rendered.", innerException);

public sealed class Phase6ValidatedFhirExportUnavailableException : Exception;
