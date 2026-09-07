using Beeexy.Domain.Common;

namespace Beeexy.Domain.Care;

public sealed class SymptomCheckInAnswer
{
    private SymptomCheckInAnswer()
    {
        SubmittedValueJson = null!;
    }

    private SymptomCheckInAnswer(
        EntityId id,
        EntityId checkInId,
        EntityId packageVersionId,
        EntityId questionId,
        string submittedValueJson,
        int sourceOrder,
        DateTimeOffset recordedAt)
    {
        Id = id;
        CheckInId = checkInId;
        PackageVersionId = packageVersionId;
        QuestionId = questionId;
        SubmittedValueJson = submittedValueJson;
        SourceOrder = sourceOrder;
        RecordedAt = recordedAt;
    }

    public EntityId Id { get; private set; }
    public EntityId CheckInId { get; private set; }
    public EntityId PackageVersionId { get; private set; }
    public EntityId QuestionId { get; private set; }
    public string SubmittedValueJson { get; private set; }
    public int SourceOrder { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    internal static SymptomCheckInAnswer Create(
        EntityId checkInId,
        EntityId packageVersionId,
        SymptomDiaryQuestion question,
        string submittedValueJson,
        DateTimeOffset recordedAt,
        EntityId? id = null)
    {
        SymptomDiaryGuard.EnsureId(checkInId, nameof(checkInId));
        SymptomDiaryGuard.EnsureId(packageVersionId, nameof(packageVersionId));
        ArgumentNullException.ThrowIfNull(question);
        if (question.PackageVersionId != packageVersionId)
        {
            throw new ArgumentException("The answer question must belong to the pinned package.", nameof(question));
        }

        InstantGuard.EnsureUtc(recordedAt, nameof(recordedAt));
        var entityId = id ?? EntityId.New();
        SymptomDiaryGuard.EnsureId(entityId, nameof(id));
        return new SymptomCheckInAnswer(
            entityId,
            checkInId,
            packageVersionId,
            question.Id,
            SymptomDiaryGuard.RequiredJson(submittedValueJson, nameof(submittedValueJson)),
            question.SourceOrder,
            recordedAt);
    }
}

public sealed record SymptomCheckInAnswerInput(
    SymptomDiaryQuestion Question,
    string SubmittedValueJson,
    EntityId? Id = null);
