namespace Beeexy.Domain.Triage;

public enum ClinicalAnswerType
{
    FreeText,
    SingleChoice,
    MultipleChoice,
    IntegerScale,
    Boolean,
    Duration,
    Temperature,
    SymptomSelection
}

public enum ClinicalQuestionPriority
{
    Ordinary = 0,
    HigherPriorityClarification = 1,
    RedFlagScreening = 2
}

public enum ClinicalConditionOperator
{
    Equals,
    ContainsAny,
    GreaterThan,
    GreaterThanOrEqual,
    ClassifiedAs
}

public sealed record ClinicalAnswerDefinition(
    ClinicalAnswerType Type,
    IReadOnlyList<string>? AllowedValues = null,
    decimal? Minimum = null,
    decimal? Maximum = null,
    string? Unit = null);

public sealed record ClinicalQuestionDefinition(
    QuestionCode Code,
    string PromptText,
    int DisplayOrder,
    ClinicalAnswerDefinition Answer,
    ClinicalQuestionPriority Priority = ClinicalQuestionPriority.Ordinary);

public sealed record ClinicalConditionDefinition(
    QuestionCode FactCode,
    ClinicalConditionOperator Operator,
    string ExpectedValue);

public sealed record ClinicalBranchDefinition(
    string Code,
    QuestionCode TriggerQuestionCode,
    ClinicalConditionOperator Operator,
    IReadOnlyList<string> ExpectedValues,
    IReadOnlyList<QuestionCode> NextQuestionCodes,
    ClinicalQuestionPriority Priority);

public sealed record ClinicalRedFlagDefinition(
    string Code,
    string Description,
    IReadOnlyList<ClinicalConditionDefinition> AllOf,
    IReadOnlyList<ClinicalConditionDefinition>? AnyOf = null);

public sealed record ClinicalRuleDefinition(
    string Code,
    UrgencyCode MinimumUrgency,
    bool IsRedFlag,
    string Description,
    IReadOnlyList<ClinicalConditionDefinition> AllOf,
    IReadOnlyList<ClinicalConditionDefinition>? AnyOf = null,
    IReadOnlyList<UrgencyCode>? RequiresAbsenceOfUrgencies = null,
    bool RequiresNoIdentifiedRedFlags = false);

public sealed record UrgencyDefinition(
    UrgencyCode Code,
    int SeverityRank,
    string Description);

public sealed record DispositionDefinition(
    string Code,
    UrgencyCode ForUrgency,
    string Recommendation);

public sealed record ClinicalRulePackageDefinition(
    IReadOnlyList<UrgencyDefinition> Urgencies,
    IReadOnlyList<DispositionDefinition> Dispositions,
    IReadOnlyList<ClinicalRedFlagDefinition> RedFlags,
    IReadOnlyList<ClinicalRuleDefinition> Rules,
    IReadOnlyList<string> ClinicalLimitations);

public sealed class ClinicalDefinitionPackage
{
    public ClinicalDefinitionPackage(
        ClinicalPathwayCode pathway,
        QuestionnaireDefinitionVersion questionnaire,
        ClinicalRuleSetVersion ruleSet,
        IReadOnlyList<ClinicalQuestionDefinition> questions,
        IReadOnlyList<ClinicalBranchDefinition> branches,
        ClinicalRulePackageDefinition ruleDefinitions)
    {
        ArgumentNullException.ThrowIfNull(pathway);
        ArgumentNullException.ThrowIfNull(questionnaire);
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(ruleDefinitions);

        if (questionnaire.Pathway != pathway || ruleSet.Pathway != pathway)
        {
            throw new ArgumentException("Package definitions must use the package pathway.");
        }

        if (questionnaire.Version != ruleSet.Version)
        {
            throw new ArgumentException("Questionnaire and rule-set package versions must match.");
        }

        if (questionnaire.ContentStatus != ruleSet.ContentStatus)
        {
            throw new ArgumentException("Questionnaire and rule-set clinical status must match.");
        }

        Pathway = pathway;
        Questionnaire = questionnaire;
        RuleSet = ruleSet;
        Questions = questions;
        Branches = branches;
        RuleDefinitions = ruleDefinitions;
    }

    public ClinicalPathwayCode Pathway { get; }

    public DefinitionVersion Version => Questionnaire.Version;

    public ClinicalContentStatus ContentStatus => Questionnaire.ContentStatus;

    public QuestionnaireDefinitionVersion Questionnaire { get; }

    public ClinicalRuleSetVersion RuleSet { get; }

    public IReadOnlyList<ClinicalQuestionDefinition> Questions { get; }

    public IReadOnlyList<ClinicalBranchDefinition> Branches { get; }

    public ClinicalRulePackageDefinition RuleDefinitions { get; }
}
