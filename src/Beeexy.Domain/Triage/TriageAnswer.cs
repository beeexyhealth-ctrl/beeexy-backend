using Beeexy.Domain.Common;

namespace Beeexy.Domain.Triage;

public sealed class TriageAnswer
{
    private TriageAnswer()
    {
        AnswerJson = null!;
    }

    private TriageAnswer(
        EntityId id,
        EntityId sessionId,
        EntityId questionnaireVersionId,
        EntityId questionId,
        string answerJson,
        int sequence,
        DateTimeOffset recordedAt)
    {
        Id = id;
        SessionId = sessionId;
        QuestionnaireVersionId = questionnaireVersionId;
        QuestionId = questionId;
        AnswerJson = answerJson;
        Sequence = sequence;
        RecordedAt = recordedAt;
    }

    public EntityId Id { get; private set; }

    public EntityId? SessionId { get; private set; }

    public EntityId? EpisodeId { get; private set; }

    public EntityId QuestionnaireVersionId { get; private set; }

    public EntityId QuestionId { get; private set; }

    public string AnswerJson { get; private set; }

    public int Sequence { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    internal static TriageAnswer CreateForSession(
        EntityId sessionId,
        EntityId questionnaireVersionId,
        EntityId questionId,
        string answerJson,
        int sequence,
        DateTimeOffset recordedAt,
        EntityId? id = null)
    {
        InstantGuard.EnsureUtc(recordedAt, nameof(recordedAt));
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Answer sequence must be positive.");
        }

        return new TriageAnswer(
            id ?? EntityId.New(),
            sessionId,
            questionnaireVersionId,
            questionId,
            TriageValueGuard.RequiredJson(answerJson, nameof(answerJson)),
            sequence,
            recordedAt);
    }

    internal void PromoteToEpisode(EntityId episodeId)
    {
        if (SessionId is null || EpisodeId is not null)
        {
            throw new InvalidOperationException("Only a temporary answer can be promoted.");
        }

        SessionId = null;
        EpisodeId = episodeId;
    }
}
