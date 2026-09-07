using Beeexy.Domain.Common;

namespace Beeexy.Domain.Care;

public sealed class SymptomDiaryQuestion
{
    private readonly List<SymptomDiaryQuestionOption> _options = [];

    private SymptomDiaryQuestion()
    {
        Code = null!;
        PromptText = null!;
        AnswerSchemaJson = null!;
    }

    private SymptomDiaryQuestion(
        EntityId id,
        EntityId packageVersionId,
        SymptomDiaryCode code,
        string promptText,
        int sourceOrder,
        string answerSchemaJson,
        bool isRequired)
    {
        Id = id;
        PackageVersionId = packageVersionId;
        Code = code;
        PromptText = promptText;
        SourceOrder = sourceOrder;
        AnswerSchemaJson = answerSchemaJson;
        IsRequired = isRequired;
    }

    public EntityId Id { get; private set; }
    public EntityId PackageVersionId { get; private set; }
    public SymptomDiaryCode Code { get; private set; }
    public string PromptText { get; private set; }
    public int SourceOrder { get; private set; }
    public string AnswerSchemaJson { get; private set; }
    public bool IsRequired { get; private set; }
    public IReadOnlyCollection<SymptomDiaryQuestionOption> Options => _options.AsReadOnly();

    internal static SymptomDiaryQuestion Create(
        EntityId packageVersionId,
        SymptomDiaryCode code,
        string promptText,
        int sourceOrder,
        string answerSchemaJson,
        bool isRequired,
        IEnumerable<SymptomDiaryQuestionOptionInput>? options = null,
        EntityId? id = null)
    {
        SymptomDiaryGuard.EnsureId(packageVersionId, nameof(packageVersionId));
        ArgumentNullException.ThrowIfNull(code);
        SymptomDiaryGuard.EnsurePositiveOrder(sourceOrder, nameof(sourceOrder));
        var entityId = id ?? EntityId.New();
        SymptomDiaryGuard.EnsureId(entityId, nameof(id));
        var question = new SymptomDiaryQuestion(
            entityId,
            packageVersionId,
            code,
            SymptomDiaryGuard.RequiredText(
                promptText,
                SymptomDiaryGuard.MaximumTextLength,
                nameof(promptText)),
            sourceOrder,
            SymptomDiaryGuard.RequiredJsonObject(answerSchemaJson, nameof(answerSchemaJson)),
            isRequired);

        foreach (var option in (options ?? []).OrderBy(value => value.SourceOrder))
        {
            ArgumentNullException.ThrowIfNull(option);
            if (question._options.Any(existing => existing.Code == option.Code))
            {
                throw new InvalidOperationException("Option codes must be unique within a question.");
            }

            if (question._options.Any(existing => existing.SourceOrder == option.SourceOrder))
            {
                throw new InvalidOperationException("Option order must be unique within a question.");
            }

            question._options.Add(SymptomDiaryQuestionOption.Create(
                entityId,
                option.Code,
                option.Value,
                option.DisplayText,
                option.SourceOrder,
                option.Id));
        }

        return question;
    }
}

public sealed record SymptomDiaryQuestionInput(
    SymptomDiaryCode Code,
    string PromptText,
    int SourceOrder,
    string AnswerSchemaJson,
    bool IsRequired,
    IReadOnlyCollection<SymptomDiaryQuestionOptionInput>? Options = null,
    EntityId? Id = null);
