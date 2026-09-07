using Beeexy.Domain.Common;

namespace Beeexy.Domain.Care;

public sealed class SymptomDiaryQuestionOption
{
    private SymptomDiaryQuestionOption()
    {
        Code = null!;
        Value = null!;
        DisplayText = null!;
    }

    private SymptomDiaryQuestionOption(
        EntityId id,
        EntityId questionId,
        SymptomDiaryCode code,
        string value,
        string displayText,
        int sourceOrder)
    {
        Id = id;
        QuestionId = questionId;
        Code = code;
        Value = value;
        DisplayText = displayText;
        SourceOrder = sourceOrder;
    }

    public EntityId Id { get; private set; }
    public EntityId QuestionId { get; private set; }
    public SymptomDiaryCode Code { get; private set; }
    public string Value { get; private set; }
    public string DisplayText { get; private set; }
    public int SourceOrder { get; private set; }

    internal static SymptomDiaryQuestionOption Create(
        EntityId questionId,
        SymptomDiaryCode code,
        string value,
        string displayText,
        int sourceOrder,
        EntityId? id = null)
    {
        SymptomDiaryGuard.EnsureId(questionId, nameof(questionId));
        ArgumentNullException.ThrowIfNull(code);
        SymptomDiaryGuard.EnsurePositiveOrder(sourceOrder, nameof(sourceOrder));
        var entityId = id ?? EntityId.New();
        SymptomDiaryGuard.EnsureId(entityId, nameof(id));
        return new SymptomDiaryQuestionOption(
            entityId,
            questionId,
            code,
            SymptomDiaryGuard.RequiredText(
                value,
                SymptomDiaryGuard.MaximumTextLength,
                nameof(value)),
            SymptomDiaryGuard.RequiredText(
                displayText,
                SymptomDiaryGuard.MaximumTextLength,
                nameof(displayText)),
            sourceOrder);
    }
}

public sealed record SymptomDiaryQuestionOptionInput(
    SymptomDiaryCode Code,
    string Value,
    string DisplayText,
    int SourceOrder,
    EntityId? Id = null);
