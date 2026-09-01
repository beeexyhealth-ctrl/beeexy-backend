using Beeexy.Domain.Common;

namespace Beeexy.Domain.Ai;

public sealed class AiResultSnapshot
{
    private AiResultSnapshot()
    {
        ResultSchemaVersion = null!;
        ContentJson = null!;
    }

    private AiResultSnapshot(
        EntityId id,
        EntityId analysisRequestId,
        EntityId executionId,
        int sequence,
        string resultSchemaVersion,
        string contentJson,
        DateTimeOffset createdAt)
    {
        Id = id;
        AnalysisRequestId = analysisRequestId;
        ExecutionId = executionId;
        Sequence = sequence;
        ResultSchemaVersion = resultSchemaVersion;
        ContentJson = contentJson;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId AnalysisRequestId { get; private set; }

    public EntityId ExecutionId { get; private set; }

    public int Sequence { get; private set; }

    public string ResultSchemaVersion { get; private set; }

    public string ContentJson { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AiResultSnapshot Create(
        EntityId analysisRequestId,
        EntityId executionId,
        int sequence,
        string resultSchemaVersion,
        string contentJson,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        AiGuard.EnsureId(analysisRequestId, nameof(analysisRequestId));
        AiGuard.EnsureId(executionId, nameof(executionId));
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                "Result snapshot sequence must be positive.");
        }

        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        return new AiResultSnapshot(
            AiGuard.IdOrNew(id, nameof(id)),
            analysisRequestId,
            executionId,
            sequence,
            AiGuard.RequiredText(
                resultSchemaVersion,
                AiPersistenceLimits.SchemaVersion,
                nameof(resultSchemaVersion)),
            AiGuard.RequiredJsonObject(contentJson, nameof(contentJson)),
            createdAt);
    }
}
