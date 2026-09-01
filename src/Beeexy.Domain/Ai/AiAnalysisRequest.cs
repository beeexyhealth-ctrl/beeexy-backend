using Beeexy.Domain.Common;

namespace Beeexy.Domain.Ai;

public sealed class AiAnalysisRequest
{
    private AiAnalysisRequest()
    {
        OriginalInputSchemaVersion = null!;
        OriginalInputSnapshotJson = null!;
    }

    private AiAnalysisRequest(
        EntityId id,
        EntityId accountId,
        EntityId? patientProfileId,
        EntityId? conversationId,
        AiAnalysisPurpose purpose,
        string originalInputSchemaVersion,
        string originalInputSnapshotJson,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        PatientProfileId = patientProfileId;
        ConversationId = conversationId;
        Purpose = purpose;
        OriginalInputSchemaVersion = originalInputSchemaVersion;
        OriginalInputSnapshotJson = originalInputSnapshotJson;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId AccountId { get; private set; }

    public EntityId? PatientProfileId { get; private set; }

    public EntityId? ConversationId { get; private set; }

    public AiAnalysisPurpose Purpose { get; private set; }

    public string OriginalInputSchemaVersion { get; private set; }

    public string OriginalInputSnapshotJson { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AiAnalysisRequest Create(
        EntityId accountId,
        AiAnalysisPurpose purpose,
        string originalInputSchemaVersion,
        string originalInputSnapshotJson,
        DateTimeOffset createdAt,
        EntityId? patientProfileId = null,
        EntityId? conversationId = null,
        EntityId? id = null)
    {
        AiGuard.EnsureId(accountId, nameof(accountId));
        AiGuard.EnsureDefined(purpose, nameof(purpose));
        if (patientProfileId.HasValue)
        {
            AiGuard.EnsureId(patientProfileId.Value, nameof(patientProfileId));
        }

        if (conversationId.HasValue)
        {
            AiGuard.EnsureId(conversationId.Value, nameof(conversationId));
        }

        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        return new AiAnalysisRequest(
            AiGuard.IdOrNew(id, nameof(id)),
            accountId,
            patientProfileId,
            conversationId,
            purpose,
            AiGuard.RequiredText(
                originalInputSchemaVersion,
                AiPersistenceLimits.SchemaVersion,
                nameof(originalInputSchemaVersion)),
            AiGuard.RequiredJsonObject(
                originalInputSnapshotJson,
                nameof(originalInputSnapshotJson)),
            createdAt);
    }
}
