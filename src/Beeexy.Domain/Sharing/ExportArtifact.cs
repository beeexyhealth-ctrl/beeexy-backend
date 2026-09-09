using Beeexy.Domain.Common;

namespace Beeexy.Domain.Sharing;

public sealed class ExportArtifact
{
    private ExportArtifact()
    {
        MediaType = null!;
        SnapshotVersion = null!;
    }

    private ExportArtifact(
        EntityId id,
        EntityId patientProfileId,
        EntityId requestedByAccountId,
        EntityId idempotencyKey,
        ExportArtifactFormat format,
        string mediaType,
        EntityId snapshotId,
        string snapshotVersion,
        DateTimeOffset createdAt,
        DateTimeOffset retentionEligibleAt)
    {
        Id = id;
        PatientProfileId = patientProfileId;
        RequestedByAccountId = requestedByAccountId;
        IdempotencyKey = idempotencyKey;
        Format = format;
        MediaType = mediaType;
        SnapshotId = snapshotId;
        SnapshotVersion = snapshotVersion;
        Status = ExportArtifactStatus.Pending;
        CreatedAt = createdAt;
        RetentionEligibleAt = retentionEligibleAt;
        Version = 1;
    }

    public EntityId Id { get; private set; }

    public EntityId PatientProfileId { get; private set; }

    public EntityId RequestedByAccountId { get; private set; }

    public EntityId IdempotencyKey { get; private set; }

    public ExportArtifactFormat Format { get; private set; }

    public string MediaType { get; private set; }

    public EntityId SnapshotId { get; private set; }

    public string SnapshotVersion { get; private set; }

    public ExportArtifactStatus Status { get; private set; }

    public string? ChecksumAlgorithm { get; private set; }

    public string? Checksum { get; private set; }

    public string? PrivateStorageIdentity { get; private set; }

    public string? FailureCategory { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset RetentionEligibleAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public long Version { get; private set; }

    public ExportArtifactContentMetadata? Content => Status is
        ExportArtifactStatus.Available or ExportArtifactStatus.Deleted
            ? ExportArtifactContentMetadata.Create(
                ChecksumAlgorithm!,
                Checksum!,
                PrivateStorageIdentity!)
            : null;

    public static ExportArtifact CreatePending(
        EntityId patientProfileId,
        EntityId requestedByAccountId,
        EntityId idempotencyKey,
        ExportArtifactFormat format,
        string mediaType,
        EntityId snapshotId,
        string snapshotVersion,
        DateTimeOffset createdAt,
        DateTimeOffset retentionEligibleAt,
        EntityId? id = null)
    {
        SharingGuard.EnsureId(patientProfileId, nameof(patientProfileId));
        SharingGuard.EnsureId(requestedByAccountId, nameof(requestedByAccountId));
        SharingGuard.EnsureId(idempotencyKey, nameof(idempotencyKey));
        SharingGuard.EnsureId(snapshotId, nameof(snapshotId));
        SharingGuard.EnsureDefined(format, nameof(format));
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        InstantGuard.EnsureUtc(retentionEligibleAt, nameof(retentionEligibleAt));
        if (retentionEligibleAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionEligibleAt),
                "Artifact retention eligibility must follow creation time.");
        }

        return new ExportArtifact(
            SharingGuard.IdOrNew(id, nameof(id)),
            patientProfileId,
            requestedByAccountId,
            idempotencyKey,
            format,
            NormalizeMediaType(mediaType),
            snapshotId,
            SharingGuard.RequiredText(
                snapshotVersion,
                SharingPersistenceLimits.SnapshotVersion,
                nameof(snapshotVersion)),
            createdAt,
            retentionEligibleAt);
    }

    public void MarkAvailable(
        ExportArtifactContentMetadata content,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureStatus(ExportArtifactStatus.Pending);
        InstantGuard.EnsureNotBefore(completedAt, CreatedAt, nameof(completedAt));

        ChecksumAlgorithm = content.ChecksumAlgorithm;
        Checksum = content.Checksum;
        PrivateStorageIdentity = content.PrivateStorageIdentity;
        CompletedAt = completedAt;
        Status = ExportArtifactStatus.Available;
        Version++;
    }

    public void MarkFailed(string failureCategory, DateTimeOffset failedAt)
    {
        EnsureStatus(ExportArtifactStatus.Pending);
        InstantGuard.EnsureNotBefore(failedAt, CreatedAt, nameof(failedAt));

        FailureCategory = SharingGuard.RequiredText(
            failureCategory,
            SharingPersistenceLimits.FailureCategory,
            nameof(failureCategory));
        FailedAt = failedAt;
        Status = ExportArtifactStatus.Failed;
        Version++;
    }

    public void MarkDeleted(DateTimeOffset deletedAt)
    {
        EnsureStatus(ExportArtifactStatus.Available);
        InstantGuard.EnsureUtc(deletedAt, nameof(deletedAt));
        if (deletedAt < RetentionEligibleAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deletedAt),
                "Artifact deletion cannot precede its retention eligibility boundary.");
        }

        DeletedAt = deletedAt;
        Status = ExportArtifactStatus.Deleted;
        Version++;
    }

    public bool IsRetentionEligibleAt(DateTimeOffset at)
    {
        InstantGuard.EnsureUtc(at, nameof(at));
        return Status == ExportArtifactStatus.Available && at >= RetentionEligibleAt;
    }

    private void EnsureStatus(ExportArtifactStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"The export artifact must be {expected} for this transition.");
        }
    }

    private static string NormalizeMediaType(string value)
    {
        var normalized = SharingGuard.RequiredText(
            value,
            SharingPersistenceLimits.MediaType,
            nameof(value));
        if (normalized.Any(char.IsWhiteSpace) ||
            normalized.Count(character => character == '/') != 1 ||
            normalized.StartsWith('/') ||
            normalized.EndsWith('/'))
        {
            throw new ArgumentException("The media type representation is invalid.", nameof(value));
        }

        return normalized.ToLowerInvariant();
    }
}
