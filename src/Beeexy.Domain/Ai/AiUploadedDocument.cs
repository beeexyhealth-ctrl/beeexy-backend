using Beeexy.Domain.Common;

namespace Beeexy.Domain.Ai;

public sealed class AiUploadedDocument
{
    private AiUploadedDocument()
    {
        StorageKey = null!;
        ContentType = null!;
    }

    private AiUploadedDocument(
        EntityId id,
        EntityId accountId,
        EntityId? patientProfileId,
        EntityId? analysisRequestId,
        string storageKey,
        string contentType,
        long sizeBytes,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        AccountId = accountId;
        PatientProfileId = patientProfileId;
        AnalysisRequestId = analysisRequestId;
        StorageKey = storageKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Status = AiDocumentStatus.Active;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public EntityId Id { get; private set; }

    public EntityId AccountId { get; private set; }

    public EntityId? PatientProfileId { get; private set; }

    public EntityId? AnalysisRequestId { get; private set; }

    public string StorageKey { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public AiDocumentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public static AiUploadedDocument Create(
        EntityId accountId,
        string storageKey,
        string contentType,
        long sizeBytes,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        EntityId? patientProfileId = null,
        EntityId? analysisRequestId = null,
        EntityId? id = null)
    {
        AiGuard.EnsureId(accountId, nameof(accountId));
        if (patientProfileId.HasValue)
        {
            AiGuard.EnsureId(patientProfileId.Value, nameof(patientProfileId));
        }

        if (analysisRequestId.HasValue)
        {
            AiGuard.EnsureId(analysisRequestId.Value, nameof(analysisRequestId));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sizeBytes),
                "Document size must be positive.");
        }

        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        InstantGuard.EnsureUtc(expiresAt, nameof(expiresAt));
        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "Document expiration must follow creation time.");
        }

        return new AiUploadedDocument(
            AiGuard.IdOrNew(id, nameof(id)),
            accountId,
            patientProfileId,
            analysisRequestId,
            AiGuard.RequiredText(
                storageKey,
                AiPersistenceLimits.StorageKey,
                nameof(storageKey)),
            AiGuard.RequiredText(
                contentType,
                AiPersistenceLimits.ContentType,
                nameof(contentType)),
            sizeBytes,
            createdAt,
            expiresAt);
    }

    public bool AssociateWithAnalysis(EntityId analysisRequestId)
    {
        AiGuard.EnsureId(analysisRequestId, nameof(analysisRequestId));
        if (Status != AiDocumentStatus.Active)
        {
            throw new InvalidOperationException(
                "Only active document metadata can be associated with an analysis.");
        }

        if (AnalysisRequestId == analysisRequestId)
        {
            return false;
        }

        if (AnalysisRequestId.HasValue)
        {
            throw new InvalidOperationException(
                "Document metadata is already associated with another analysis.");
        }

        AnalysisRequestId = analysisRequestId;
        return true;
    }

    public bool MarkDeleted(DateTimeOffset deletedAt)
    {
        return MarkUnavailable(AiDocumentStatus.Deleted, deletedAt, requireExpiry: false);
    }

    public bool MarkExpired(DateTimeOffset deletedAt)
    {
        return MarkUnavailable(AiDocumentStatus.Expired, deletedAt, requireExpiry: true);
    }

    private bool MarkUnavailable(
        AiDocumentStatus status,
        DateTimeOffset deletedAt,
        bool requireExpiry)
    {
        InstantGuard.EnsureNotBefore(deletedAt, CreatedAt, nameof(deletedAt));
        if (Status != AiDocumentStatus.Active)
        {
            return false;
        }

        if (requireExpiry && deletedAt < ExpiresAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deletedAt),
                "Document expiry deletion cannot precede its expiry time.");
        }

        Status = status;
        DeletedAt = deletedAt;
        return true;
    }
}
