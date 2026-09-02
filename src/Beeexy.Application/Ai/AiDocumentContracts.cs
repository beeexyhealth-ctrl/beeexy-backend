using System.Security.Cryptography;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Ai;

public sealed class AiDocumentOptions
{
    public const long MaximumAllowedBytes = 26_214_400;
    public static readonly TimeSpan MaximumRetention = TimeSpan.FromHours(24);

    public AiDocumentOptions(long maximumBytes, TimeSpan cleanupCadence, int cleanupBatchSize)
    {
        if (maximumBytes <= 0 || maximumBytes > MaximumAllowedBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        if (cleanupCadence <= TimeSpan.Zero || cleanupCadence > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(cleanupCadence));
        }

        if (cleanupBatchSize is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(cleanupBatchSize));
        }

        MaximumBytes = maximumBytes;
        CleanupCadence = cleanupCadence;
        CleanupBatchSize = cleanupBatchSize;
    }

    public long MaximumBytes { get; }
    public TimeSpan CleanupCadence { get; }
    public int CleanupBatchSize { get; }
}

public sealed record AiBlobKey
{
    private const int KeyBytes = 32;

    private AiBlobKey(string value) => Value = value;

    public string Value { get; }

    public static AiBlobKey CreateNew() => new(
        Convert.ToHexString(RandomNumberGenerator.GetBytes(KeyBytes)).ToLowerInvariant());

    public static AiBlobKey Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != KeyBytes * 2 || value.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("The private blob key is invalid.", nameof(value));
        }

        return new AiBlobKey(value);
    }
}

public interface IAiDocumentBlobStore
{
    Task WritePrivateAsync(
        AiBlobKey key,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);

    Task<byte[]> ReadPrivateAsync(
        AiBlobKey key,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        AiBlobKey key,
        CancellationToken cancellationToken = default);

    Task<int> DeleteCreatedBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);
}

public enum AiFileSafetyStatus
{
    Safe,
    Unsafe,
    Indeterminate
}

public sealed record AiFileSafetyResult(AiFileSafetyStatus Status);

public interface IAiDocumentSafetyScanner
{
    Task<AiFileSafetyResult> ScanAsync(
        ReadOnlyMemory<byte> content,
        string normalizedContentType,
        CancellationToken cancellationToken = default);
}

public enum AiDocumentExtractionStatus
{
    Success,
    Unsupported,
    Malformed,
    NoUsefulText,
    Failed
}

public sealed record AiDocumentExtractionResult(
    AiDocumentExtractionStatus Status,
    string? ExtractedText = null);

public interface IAiDocumentTextExtractor
{
    Task<AiDocumentExtractionResult> ExtractAsync(
        ReadOnlyMemory<byte> content,
        string normalizedContentType,
        CancellationToken cancellationToken = default);
}

public interface IAiDocumentRepository
{
    void Add(AiUploadedDocument document);

    Task<AiUploadedDocument?> FindOwnedAsync(
        EntityId documentId,
        EntityId accountId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiUploadedDocument>> ListExpiredAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetNextExpiryAsync(CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record UploadAiDocumentCommand(
    string FileName,
    string DeclaredContentType,
    long DeclaredSizeBytes,
    ReadOnlyMemory<byte> Content);

public sealed record AiDocumentMetadata(
    EntityId DocumentId,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    DateTimeOffset ExpiresAt,
    AiDocumentStatus Status);

public sealed class AiDocumentTooLargeException : Exception;

public sealed class AiDocumentUnsupportedMediaException : Exception;

public sealed class AiDocumentValidationException(
    string code,
    string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class AiDocumentNotFoundException : Exception;
