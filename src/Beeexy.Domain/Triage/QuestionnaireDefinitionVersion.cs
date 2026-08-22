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
        ClinicalPathwayCode pathway,
        QuestionnaireCode questionnaireCode,
        DefinitionVersion version,
        DefinitionHash contentHash,
        ClinicalContentStatus contentStatus,
        string? sourceReference,
        DateTimeOffset importedAt,
        DateTimeOffset? approvedAt,
        DateTimeOffset? activatedAt)
    {
        Id = id;
        Pathway = pathway;
        QuestionnaireCode = questionnaireCode;
        Version = version;
        ContentHash = contentHash;
        ContentSource = contentStatus.Source;
        ReviewStatus = contentStatus.ReviewStatus;
        ApprovalStatus = contentStatus.ApprovalStatus;
        SourceReference = sourceReference;
        ImportedAt = importedAt;
        ApprovedAt = approvedAt;
        ActivatedAt = activatedAt;
    }

    public EntityId Id { get; private set; }

    public ClinicalPathwayCode Pathway { get; private set; } = null!;

    public QuestionnaireCode QuestionnaireCode { get; private set; }

    public DefinitionVersion Version { get; private set; }

    public DefinitionHash ContentHash { get; private set; }

    public ClinicalContentSource ContentSource { get; private set; }

    public ClinicalReviewStatus ReviewStatus { get; private set; }

    public ClinicalApprovalStatus ApprovalStatus { get; private set; }

    public ClinicalContentStatus ContentStatus => new(
        ContentSource,
        ReviewStatus,
        ApprovalStatus);

    public string? SourceReference { get; private set; }

    public DateTimeOffset ImportedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

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
        return Import(
            ClinicalPathwayCode.Create("UNSPECIFIED"),
            questionnaireCode,
            version,
            contentHash,
            ClinicalContentStatus.LegacyApproved,
            importedAt,
            approvedAt,
            activatedAt,
            sourceReference,
            id,
            questions);
    }

    public static QuestionnaireDefinitionVersion Import(
        ClinicalPathwayCode pathway,
        QuestionnaireCode questionnaireCode,
        DefinitionVersion version,
        DefinitionHash contentHash,
        ClinicalContentStatus contentStatus,
        DateTimeOffset importedAt,
        DateTimeOffset? approvedAt = null,
        DateTimeOffset? activatedAt = null,
        string? sourceReference = null,
        EntityId? id = null,
        IEnumerable<TriageQuestionInput>? questions = null)
    {
        ArgumentNullException.ThrowIfNull(pathway);
        ArgumentNullException.ThrowIfNull(questionnaireCode);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(contentHash);
        ArgumentNullException.ThrowIfNull(contentStatus);
        InstantGuard.EnsureUtc(importedAt, nameof(importedAt));
        if (approvedAt.HasValue)
        {
            InstantGuard.EnsureUtc(approvedAt.Value, nameof(approvedAt));
        }

        if (contentStatus.ApprovalStatus == ClinicalApprovalStatus.Approved &&
            !approvedAt.HasValue)
        {
            throw new ArgumentException(
                "Approved clinical content requires an approval timestamp.",
                nameof(approvedAt));
        }

        if (contentStatus.ApprovalStatus != ClinicalApprovalStatus.Approved &&
            approvedAt.HasValue)
        {
            throw new ArgumentException(
                "Unapproved clinical content cannot have an approval timestamp.",
                nameof(approvedAt));
        }

        if (activatedAt.HasValue)
        {
            InstantGuard.EnsureUtc(activatedAt.Value, nameof(activatedAt));
            if (activatedAt < importedAt ||
                (approvedAt.HasValue && activatedAt < approvedAt.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activatedAt),
                    "Activation cannot precede import or approval.");
            }
        }

        var questionnaire = new QuestionnaireDefinitionVersion(
            id ?? EntityId.New(),
            pathway,
            questionnaireCode,
            version,
            contentHash,
            contentStatus,
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
