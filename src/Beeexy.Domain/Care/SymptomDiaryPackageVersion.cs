using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Domain.Care;

public sealed class SymptomDiaryPackageVersion
{
    private readonly List<SymptomDiaryQuestion> _questions = [];
    private readonly List<SymptomWarningSign> _warningSigns = [];

    private SymptomDiaryPackageVersion()
    {
        PackageCode = null!;
        PackageVersion = null!;
        QuestionSetCode = null!;
        QuestionSetVersion = null!;
        SymptomInformationCode = null!;
        SymptomInformationVersion = null!;
        Pathway = null!;
        CanonicalContentHash = null!;
    }

    private SymptomDiaryPackageVersion(
        EntityId id,
        SymptomDiaryCode packageCode,
        DefinitionVersion packageVersion,
        SymptomDiaryCode questionSetCode,
        DefinitionVersion questionSetVersion,
        SymptomDiaryCode symptomInformationCode,
        DefinitionVersion symptomInformationVersion,
        ClinicalPathwayCode pathway,
        SymptomDiarySha256 canonicalContentHash,
        ClinicalContentStatus contentStatus,
        string? sourceReference,
        DateTimeOffset importedAt,
        DateTimeOffset? approvedAt,
        DateTimeOffset? activatedAt,
        string? informationalHeading,
        string? informationalBody)
    {
        Id = id;
        PackageCode = packageCode;
        PackageVersion = packageVersion;
        QuestionSetCode = questionSetCode;
        QuestionSetVersion = questionSetVersion;
        SymptomInformationCode = symptomInformationCode;
        SymptomInformationVersion = symptomInformationVersion;
        Pathway = pathway;
        CanonicalContentHash = canonicalContentHash;
        ContentSource = contentStatus.Source;
        ReviewStatus = contentStatus.ReviewStatus;
        ApprovalStatus = contentStatus.ApprovalStatus;
        SourceReference = sourceReference;
        ImportedAt = importedAt;
        ApprovedAt = approvedAt;
        ActivatedAt = activatedAt;
        InformationalHeading = informationalHeading;
        InformationalBody = informationalBody;
    }

    public EntityId Id { get; private set; }
    public SymptomDiaryCode PackageCode { get; private set; }
    public DefinitionVersion PackageVersion { get; private set; }
    public SymptomDiaryCode QuestionSetCode { get; private set; }
    public DefinitionVersion QuestionSetVersion { get; private set; }
    public SymptomDiaryCode SymptomInformationCode { get; private set; }
    public DefinitionVersion SymptomInformationVersion { get; private set; }
    public ClinicalPathwayCode Pathway { get; private set; }
    public SymptomDiarySha256 CanonicalContentHash { get; private set; }
    public ClinicalContentSource ContentSource { get; private set; }
    public ClinicalReviewStatus ReviewStatus { get; private set; }
    public ClinicalApprovalStatus ApprovalStatus { get; private set; }
    public ClinicalContentStatus ContentStatus => new(ContentSource, ReviewStatus, ApprovalStatus);
    public string? SourceReference { get; private set; }
    public DateTimeOffset ImportedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public string? InformationalHeading { get; private set; }
    public string? InformationalBody { get; private set; }
    public IReadOnlyCollection<SymptomDiaryQuestion> Questions => _questions.AsReadOnly();
    public IReadOnlyCollection<SymptomWarningSign> WarningSigns => _warningSigns.AsReadOnly();

    public static SymptomDiaryPackageVersion Import(
        SymptomDiaryCode packageCode,
        DefinitionVersion packageVersion,
        SymptomDiaryCode questionSetCode,
        DefinitionVersion questionSetVersion,
        SymptomDiaryCode symptomInformationCode,
        DefinitionVersion symptomInformationVersion,
        ClinicalPathwayCode pathway,
        SymptomDiarySha256 canonicalContentHash,
        ClinicalContentStatus contentStatus,
        DateTimeOffset importedAt,
        DateTimeOffset? approvedAt = null,
        DateTimeOffset? activatedAt = null,
        string? sourceReference = null,
        string? informationalHeading = null,
        string? informationalBody = null,
        IEnumerable<SymptomDiaryQuestionInput>? questions = null,
        IEnumerable<SymptomWarningSignInput>? warningSigns = null,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(packageCode);
        ArgumentNullException.ThrowIfNull(packageVersion);
        ArgumentNullException.ThrowIfNull(questionSetCode);
        ArgumentNullException.ThrowIfNull(questionSetVersion);
        ArgumentNullException.ThrowIfNull(symptomInformationCode);
        ArgumentNullException.ThrowIfNull(symptomInformationVersion);
        ArgumentNullException.ThrowIfNull(pathway);
        ArgumentNullException.ThrowIfNull(canonicalContentHash);
        ArgumentNullException.ThrowIfNull(contentStatus);
        InstantGuard.EnsureUtc(importedAt, nameof(importedAt));
        ValidateLifecycle(contentStatus, importedAt, approvedAt, activatedAt);
        var entityId = id ?? EntityId.New();
        SymptomDiaryGuard.EnsureId(entityId, nameof(id));
        var package = new SymptomDiaryPackageVersion(
            entityId,
            packageCode,
            packageVersion,
            questionSetCode,
            questionSetVersion,
            symptomInformationCode,
            symptomInformationVersion,
            pathway,
            canonicalContentHash,
            contentStatus,
            SymptomDiaryGuard.OptionalText(
                sourceReference,
                SymptomDiaryGuard.MaximumReferenceLength,
                nameof(sourceReference)),
            importedAt,
            approvedAt,
            activatedAt,
            SymptomDiaryGuard.OptionalText(
                informationalHeading,
                SymptomDiaryGuard.MaximumTextLength,
                nameof(informationalHeading)),
            SymptomDiaryGuard.OptionalText(
                informationalBody,
                SymptomDiaryGuard.MaximumBodyLength,
                nameof(informationalBody)));

        foreach (var question in (questions ?? []).OrderBy(value => value.SourceOrder))
        {
            ArgumentNullException.ThrowIfNull(question);
            if (package._questions.Any(existing => existing.Code == question.Code))
            {
                throw new InvalidOperationException("Question codes must be unique within a package.");
            }

            if (package._questions.Any(existing => existing.SourceOrder == question.SourceOrder))
            {
                throw new InvalidOperationException("Question order must be unique within a package.");
            }

            package._questions.Add(SymptomDiaryQuestion.Create(
                entityId,
                question.Code,
                question.PromptText,
                question.SourceOrder,
                question.AnswerSchemaJson,
                question.IsRequired,
                question.Options,
                question.Id));
        }

        foreach (var warningSign in (warningSigns ?? []).OrderBy(value => value.SourceOrder))
        {
            ArgumentNullException.ThrowIfNull(warningSign);
            if (package._warningSigns.Any(existing => existing.Code == warningSign.Code))
            {
                throw new InvalidOperationException(
                    "Warning-sign codes must be unique within a package.");
            }

            if (package._warningSigns.Any(existing =>
                    existing.SourceOrder == warningSign.SourceOrder))
            {
                throw new InvalidOperationException(
                    "Warning-sign order must be unique within a package.");
            }

            package._warningSigns.Add(SymptomWarningSign.Create(
                entityId,
                warningSign.Code,
                warningSign.DisplayText,
                warningSign.SourceOrder,
                warningSign.Id));
        }

        return package;
    }

    private static void ValidateLifecycle(
        ClinicalContentStatus contentStatus,
        DateTimeOffset importedAt,
        DateTimeOffset? approvedAt,
        DateTimeOffset? activatedAt)
    {
        if (approvedAt.HasValue)
        {
            InstantGuard.EnsureUtc(approvedAt.Value, nameof(approvedAt));
        }

        if (contentStatus.ApprovalStatus == ClinicalApprovalStatus.Approved !=
            approvedAt.HasValue)
        {
            throw new ArgumentException(
                "Approval status and approval timestamp must agree.",
                nameof(approvedAt));
        }

        if (activatedAt.HasValue)
        {
            InstantGuard.EnsureUtc(activatedAt.Value, nameof(activatedAt));
            if (activatedAt.Value < importedAt ||
                (approvedAt.HasValue && activatedAt.Value < approvedAt.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activatedAt),
                    "Activation cannot precede import or approval.");
            }
        }
    }
}
