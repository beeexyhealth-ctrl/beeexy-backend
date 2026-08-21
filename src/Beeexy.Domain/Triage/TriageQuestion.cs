using Beeexy.Domain.Common;

namespace Beeexy.Domain.Triage;

public sealed class TriageQuestion
{
    public const int MaximumPromptLength = 4000;

    private TriageQuestion()
    {
        Code = null!;
        PromptText = null!;
    }

    private TriageQuestion(
        EntityId id,
        EntityId questionnaireVersionId,
        QuestionCode code,
        string promptText,
        int displayOrder,
        string? answerSchemaJson,
        string? branchingMetadataJson,
        DateTimeOffset createdAt)
    {
        Id = id;
        QuestionnaireVersionId = questionnaireVersionId;
        Code = code;
        PromptText = promptText;
        DisplayOrder = displayOrder;
        AnswerSchemaJson = answerSchemaJson;
        BranchingMetadataJson = branchingMetadataJson;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId QuestionnaireVersionId { get; private set; }

    public QuestionCode Code { get; private set; }

    public string PromptText { get; private set; }

    public int DisplayOrder { get; private set; }

    public string? AnswerSchemaJson { get; private set; }

    public string? BranchingMetadataJson { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static TriageQuestion Create(
        EntityId questionnaireVersionId,
        QuestionCode code,
        string promptText,
        int displayOrder,
        DateTimeOffset createdAt,
        string? answerSchemaJson = null,
        string? branchingMetadataJson = null,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        if (questionnaireVersionId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "A questionnaire-version identifier is required.",
                nameof(questionnaireVersionId));
        }

        if (displayOrder <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(displayOrder),
                "Question order must be positive.");
        }

        return new TriageQuestion(
            id ?? EntityId.New(),
            questionnaireVersionId,
            code,
            TriageValueGuard.RequiredText(promptText, MaximumPromptLength, nameof(promptText)),
            displayOrder,
            TriageValueGuard.OptionalJson(answerSchemaJson, nameof(answerSchemaJson)),
            TriageValueGuard.OptionalJson(branchingMetadataJson, nameof(branchingMetadataJson)),
            createdAt);
    }
}
