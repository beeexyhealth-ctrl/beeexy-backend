using Beeexy.Domain.Common;

namespace Beeexy.Domain.Triage;

public sealed class QuestionnaireDefinitionVersion
{
    private readonly List<TriageQuestion> _questions = [];

    private QuestionnaireDefinitionVersion()
    {
        QuestionnaireCode = null!;
        Version = null!;
        ContentHash = null!;
    }

    private QuestionnaireDefinitionVersion(
        EntityId id,
        QuestionnaireCode questionnaireCode,
        DefinitionVersion version,
        DefinitionHash contentHash,
        string? sourceReference,
        DateTimeOffset importedAt,
        DateTimeOffset approvedAt,
        DateTimeOffset? activatedAt)
    {
        Id = id;
        QuestionnaireCode = questionnaireCode;
        Version = version;
        ContentHash = contentHash;
        SourceReference = sourceReference;
        ImportedAt = importedAt;
        ApprovedAt = approvedAt;
        ActivatedAt = activatedAt;
    }

    public EntityId Id { get; private set; }

    public QuestionnaireCode QuestionnaireCode { get; private set; }

    public DefinitionVersion Version { get; private set; }

    public DefinitionHash ContentHash { get; private set; }

    public string? SourceReference { get; private set; }

    public DateTimeOffset ImportedAt { get; private set; }

    public DateTimeOffset ApprovedAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public IReadOnlyCollection<TriageQuestion> Questions => _questions.AsReadOnly();

    public static QuestionnaireDefinitionVersion ImportApproved(
        QuestionnaireCode questionnaireCode,
        DefinitionVersion version,
        DefinitionHash contentHash,
        DateTimeOffset importedAt,
        DateTimeOffset approvedAt,
        DateTimeOffset? activatedAt = null,
        string? sourceReference = null,
        EntityId? id = null,
        IEnumerable<TriageQuestionInput>? questions = null)
    {
        ArgumentNullException.ThrowIfNull(questionnaireCode);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(contentHash);
        InstantGuard.EnsureUtc(importedAt, nameof(importedAt));
        InstantGuard.EnsureUtc(approvedAt, nameof(approvedAt));
        if (activatedAt.HasValue)
        {
            InstantGuard.EnsureUtc(activatedAt.Value, nameof(activatedAt));
            if (activatedAt < importedAt || activatedAt < approvedAt)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activatedAt),
                    "Activation cannot precede import or approval.");
            }
        }

        var questionnaire = new QuestionnaireDefinitionVersion(
            id ?? EntityId.New(),
            questionnaireCode,
            version,
            contentHash,
            TriageValueGuard.OptionalText(
                sourceReference,
                TriagePersistenceLimits.MaximumReferenceLength,
                nameof(sourceReference)),
            importedAt,
            approvedAt,
            activatedAt);
        if (questions is null)
        {
            return questionnaire;
        }

        foreach (var question in questions)
        {
            ArgumentNullException.ThrowIfNull(question);
            questionnaire.AddQuestion(
                question.Code,
                question.PromptText,
                question.DisplayOrder,
                importedAt,
                question.AnswerSchemaJson,
                question.BranchingMetadataJson,
                question.Id);
        }

        return questionnaire;
    }

    private void AddQuestion(
        QuestionCode code,
        string promptText,
        int displayOrder,
        DateTimeOffset createdAt,
        string? answerSchemaJson = null,
        string? branchingMetadataJson = null,
        EntityId? id = null)
    {
        if (_questions.Any(question => question.Code == code))
        {
            throw new InvalidOperationException("Question codes must be unique within a version.");
        }

        if (_questions.Any(question => question.DisplayOrder == displayOrder))
        {
            throw new InvalidOperationException("Question order must be unique within a version.");
        }

        _questions.Add(TriageQuestion.Create(
            Id,
            code,
            promptText,
            displayOrder,
            createdAt,
            answerSchemaJson,
            branchingMetadataJson,
            id));
    }
}
