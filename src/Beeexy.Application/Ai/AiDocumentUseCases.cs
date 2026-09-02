using Beeexy.Application.Identity;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Ai;

public sealed class UploadAiDocument(
    ICurrentSessionIdentity currentIdentity,
    IClock clock,
    AiDocumentOptions options,
    IAiDocumentSafetyScanner safetyScanner,
    IAiDocumentTextExtractor textExtractor,
    IAiDocumentBlobStore blobStore,
    IAiDocumentRepository repository)
{
    public async Task<AiDocumentMetadata> ExecuteAsync(
        UploadAiDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var identity = currentIdentity.GetRequired();
        ValidateSize(command);
        var normalizedContentType = ResolveContentType(command);

        AiFileSafetyResult safety;
        try
        {
            safety = await safetyScanner.ScanAsync(
                command.Content,
                normalizedContentType,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            safety = new AiFileSafetyResult(AiFileSafetyStatus.Indeterminate);
        }

        if (safety.Status != AiFileSafetyStatus.Safe)
        {
            throw new AiDocumentValidationException(
                "ai.document.file_unsafe",
                "The document could not be established as safe. Choose another document.");
        }

        AiDocumentExtractionResult extraction;
        try
        {
            extraction = await textExtractor.ExtractAsync(
                command.Content,
                normalizedContentType,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            extraction = new AiDocumentExtractionResult(AiDocumentExtractionStatus.Failed);
        }
        if (extraction.Status != AiDocumentExtractionStatus.Success ||
            string.IsNullOrWhiteSpace(extraction.ExtractedText))
        {
            throw new AiDocumentValidationException(
                "ai.document.unusable_text",
                "Beeexy could not extract useful text. Choose another supported document.");
        }

        var now = clock.UtcNow;
        var blobKey = AiBlobKey.CreateNew();
        var document = AiUploadedDocument.Create(
            identity.AccountId,
            blobKey.Value,
            normalizedContentType,
            command.Content.Length,
            now,
            now.Add(AiDocumentOptions.MaximumRetention));

        await blobStore.WritePrivateAsync(blobKey, command.Content, cancellationToken);
        try
        {
            repository.Add(document);
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await CompensateBlobAsync(blobKey);
            throw;
        }

        return ToMetadata(document);
    }

    private void ValidateSize(UploadAiDocumentCommand command)
    {
        if (command.DeclaredSizeBytes <= 0 || command.Content.Length <= 0)
        {
            throw new AiDocumentValidationException(
                "ai.document.empty",
                "Choose a non-empty supported document.");
        }

        if (command.DeclaredSizeBytes > options.MaximumBytes ||
            command.Content.Length > options.MaximumBytes)
        {
            throw new AiDocumentTooLargeException();
        }

        if (command.DeclaredSizeBytes != command.Content.Length)
        {
            throw new AiDocumentValidationException(
                "ai.document.size_mismatch",
                "The document size could not be validated.");
        }
    }

    private static string ResolveContentType(UploadAiDocumentCommand command)
    {
        var extension = Path.GetExtension(command.FileName);
        var declaredType = command.DeclaredContentType.Split(';', 2)[0].Trim();
        var bytes = command.Content.Span;
        var hasPdfSignature = bytes.Length >= 5 && bytes[..5].SequenceEqual("%PDF-"u8);

        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(declaredType, "application/pdf", StringComparison.OrdinalIgnoreCase) &&
            hasPdfSignature)
        {
            return "application/pdf";
        }

        if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(declaredType, "text/plain", StringComparison.OrdinalIgnoreCase) &&
            !hasPdfSignature)
        {
            return "text/plain";
        }

        throw new AiDocumentUnsupportedMediaException();
    }

    private async Task CompensateBlobAsync(AiBlobKey key)
    {
        try
        {
            await blobStore.DeleteAsync(key, CancellationToken.None);
        }
        catch
        {
            // Preserve the original persistence failure. The private store performs
            // idempotent best-effort deletion and never exposes the artifact publicly.
        }
    }

    internal static AiDocumentMetadata ToMetadata(AiUploadedDocument document) => new(
        document.Id,
        document.ContentType,
        document.SizeBytes,
        document.CreatedAt,
        document.ExpiresAt,
        document.Status);
}

public sealed class DeleteAiDocument(
    ICurrentSessionIdentity currentIdentity,
    IClock clock,
    IAiDocumentBlobStore blobStore,
    IAiDocumentRepository repository)
{
    public async Task ExecuteAsync(
        EntityId documentId,
        CancellationToken cancellationToken = default)
    {
        var identity = currentIdentity.GetRequired();
        var document = await repository.FindOwnedAsync(
            documentId,
            identity.AccountId,
            cancellationToken) ?? throw new AiDocumentNotFoundException();

        if (document.Status != AiDocumentStatus.Active)
        {
            return;
        }

        await blobStore.DeleteAsync(AiBlobKey.Parse(document.StorageKey), cancellationToken);
        document.MarkDeleted(clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ExpireAiDocuments(
    IClock clock,
    AiDocumentOptions options,
    IAiDocumentBlobStore blobStore,
    IAiDocumentRepository repository)
{
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var documents = await repository.ListExpiredAsync(
            now,
            options.CleanupBatchSize,
            cancellationToken);
        var expired = 0;
        var deletionFailures = new List<Exception>();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document.Status != AiDocumentStatus.Active || document.ExpiresAt > now)
            {
                continue;
            }

            try
            {
                await blobStore.DeleteAsync(
                    AiBlobKey.Parse(document.StorageKey),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                deletionFailures.Add(exception);
                continue;
            }

            if (document.MarkExpired(now))
            {
                await repository.SaveChangesAsync(cancellationToken);
                expired++;
            }
        }

        try
        {
            await blobStore.DeleteCreatedBeforeAsync(
                now.Subtract(AiDocumentOptions.MaximumRetention),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            deletionFailures.Add(exception);
        }

        if (deletionFailures.Count > 0)
        {
            throw new AggregateException(
                "One or more private temporary artifacts remain retryable.",
                deletionFailures);
        }

        return expired;
    }
}
