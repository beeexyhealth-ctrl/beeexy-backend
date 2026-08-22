using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Domain.Triage;

namespace Beeexy.Infrastructure.Triage;

internal static class ClinicalDefinitionSerialization
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
    };

    public static string SerializeQuestion(ClinicalQuestionDefinition question)
    {
        return JsonSerializer.Serialize(
            new QuestionMetadataDto(question.Answer, question.Priority),
            Options);
    }

    public static ClinicalQuestionDefinition DeserializeQuestion(TriageQuestion question)
    {
        var metadata = JsonSerializer.Deserialize<QuestionMetadataDto>(
            question.AnswerSchemaJson ?? throw new InvalidOperationException(
                $"Question '{question.Code}' has no answer metadata."),
            Options) ?? throw new InvalidOperationException(
                $"Question '{question.Code}' has invalid answer metadata.");
        return new ClinicalQuestionDefinition(
            question.Code,
            question.PromptText,
            question.DisplayOrder,
            metadata.Answer,
            metadata.Priority);
    }

    public static string? SerializeBranches(IEnumerable<ClinicalBranchDefinition> branches)
    {
        var values = branches.Select(branch => new BranchDto(
            branch.Code,
            branch.TriggerQuestionCode.Value,
            branch.Operator,
            branch.ExpectedValues,
            branch.NextQuestionCodes.Select(code => code.Value).ToArray(),
            branch.Priority)).ToArray();
        return values.Length == 0 ? null : JsonSerializer.Serialize(values, Options);
    }

    public static IReadOnlyList<ClinicalBranchDefinition> DeserializeBranches(
        IEnumerable<TriageQuestion> questions)
    {
        return questions
            .Where(question => question.BranchingMetadataJson is not null)
            .SelectMany(question =>
                JsonSerializer.Deserialize<BranchDto[]>(
                    question.BranchingMetadataJson!,
                    Options) ?? throw new InvalidOperationException(
                        $"Question '{question.Code}' has invalid branch metadata."))
            .Select(branch => new ClinicalBranchDefinition(
                branch.Code,
                QuestionCode.Create(branch.TriggerQuestionCode),
                branch.Operator,
                branch.ExpectedValues,
                branch.NextQuestionCodes.Select(QuestionCode.Create).ToArray(),
                branch.Priority))
            .ToArray();
    }

    public static string SerializeRulePackage(ClinicalRulePackageDefinition package)
    {
        return JsonSerializer.Serialize(ToDto(package), Options);
    }

    public static ClinicalRulePackageDefinition DeserializeRulePackage(string json)
    {
        var package = JsonSerializer.Deserialize<RulePackageDto>(json, Options) ??
            throw new InvalidOperationException("The clinical rule package metadata is invalid.");
        return new ClinicalRulePackageDefinition(
            package.Urgencies.Select(value => new UrgencyDefinition(
                UrgencyCode.Create(value.Code),
                value.SeverityRank,
                value.Description)).ToArray(),
            package.Dispositions.Select(value => new DispositionDefinition(
                value.Code,
                UrgencyCode.Create(value.ForUrgency),
                value.Recommendation)).ToArray(),
            package.RedFlags.Select(value => new ClinicalRedFlagDefinition(
                value.Code,
                value.Description,
                FromConditions(value.AllOf),
                FromConditions(value.AnyOf))).ToArray(),
            package.Rules.Select(value => new ClinicalRuleDefinition(
                value.Code,
                UrgencyCode.Create(value.MinimumUrgency),
                value.IsRedFlag,
                value.Description,
                FromConditions(value.AllOf),
                FromConditions(value.AnyOf),
                value.RequiresAbsenceOfUrgencies?.Select(UrgencyCode.Create).ToArray(),
                value.RequiresNoIdentifiedRedFlags)).ToArray(),
            package.ClinicalLimitations)
        {
            Profile = package.Profile,
            DemoIntake = package.DemoIntake is null
                ? null
                : new DemoIntakePackageDefinition(
                    package.DemoIntake.PrimarySymptomDisplayLabel,
                    QuestionCode.Create(package.DemoIntake.PrimarySymptomQuestionCode),
                    QuestionCode.Create(package.DemoIntake.DurationQuestionCode),
                    QuestionCode.Create(package.DemoIntake.IntensityQuestionCode),
                    QuestionCode.Create(package.DemoIntake.AdditionalSymptomsQuestionCode),
                    package.DemoIntake.AdditionalSymptomCatalog,
                    package.DemoIntake.ApplicableAdditionalSymptoms,
                    package.DemoIntake.RequiredAnswerQuestionCodes
                        .Select(QuestionCode.Create).ToArray(),
                    package.DemoIntake.ProgressionQuestionCodes
                        .Select(QuestionCode.Create).ToArray(),
                    package.DemoIntake.AdditionalSymptomsAllowsEmptySelection)
        };
    }

    private static RulePackageDto ToDto(ClinicalRulePackageDefinition package)
    {
        return new RulePackageDto(
            package.Urgencies.Select(value => new UrgencyDto(
                value.Code.Value,
                value.SeverityRank,
                value.Description)).ToArray(),
            package.Dispositions.Select(value => new DispositionDto(
                value.Code,
                value.ForUrgency.Value,
                value.Recommendation)).ToArray(),
            package.RedFlags.Select(value => new RedFlagDto(
                value.Code,
                value.Description,
                ToConditions(value.AllOf),
                ToConditions(value.AnyOf))).ToArray(),
            package.Rules.Select(value => new RuleDto(
                value.Code,
                value.MinimumUrgency.Value,
                value.IsRedFlag,
                value.Description,
                ToConditions(value.AllOf),
                ToConditions(value.AnyOf),
                value.RequiresAbsenceOfUrgencies?.Select(code => code.Value).ToArray(),
                value.RequiresNoIdentifiedRedFlags)).ToArray(),
            package.ClinicalLimitations)
        {
            Profile = package.Profile,
            DemoIntake = package.DemoIntake is null
                ? null
                : new DemoIntakePackageDto(
                    package.DemoIntake.PrimarySymptomDisplayLabel,
                    package.DemoIntake.PrimarySymptomQuestionCode.Value,
                    package.DemoIntake.DurationQuestionCode.Value,
                    package.DemoIntake.IntensityQuestionCode.Value,
                    package.DemoIntake.AdditionalSymptomsQuestionCode.Value,
                    package.DemoIntake.AdditionalSymptomCatalog,
                    package.DemoIntake.ApplicableAdditionalSymptoms,
                    package.DemoIntake.RequiredAnswerQuestionCodes
                        .Select(value => value.Value).ToArray(),
                    package.DemoIntake.ProgressionQuestionCodes
                        .Select(value => value.Value).ToArray(),
                    package.DemoIntake.AdditionalSymptomsAllowsEmptySelection)
        };
    }

    private static ConditionDto[] ToConditions(
        IReadOnlyList<ClinicalConditionDefinition>? conditions)
    {
        return conditions?.Select(value => new ConditionDto(
            value.FactCode.Value,
            value.Operator,
            value.ExpectedValue)).ToArray() ?? [];
    }

    private static ClinicalConditionDefinition[] FromConditions(
        IReadOnlyList<ConditionDto>? conditions)
    {
        return conditions?.Select(value => new ClinicalConditionDefinition(
            QuestionCode.Create(value.FactCode),
            value.Operator,
            value.ExpectedValue)).ToArray() ?? [];
    }

    private sealed record QuestionMetadataDto(
        ClinicalAnswerDefinition Answer,
        ClinicalQuestionPriority Priority);

    private sealed record BranchDto(
        string Code,
        string TriggerQuestionCode,
        ClinicalConditionOperator Operator,
        IReadOnlyList<string> ExpectedValues,
        IReadOnlyList<string> NextQuestionCodes,
        ClinicalQuestionPriority Priority);

    private sealed record ConditionDto(
        string FactCode,
        ClinicalConditionOperator Operator,
        string ExpectedValue);

    private sealed record RedFlagDto(
        string Code,
        string Description,
        IReadOnlyList<ConditionDto> AllOf,
        IReadOnlyList<ConditionDto>? AnyOf);

    private sealed record RuleDto(
        string Code,
        string MinimumUrgency,
        bool IsRedFlag,
        string Description,
        IReadOnlyList<ConditionDto> AllOf,
        IReadOnlyList<ConditionDto>? AnyOf,
        IReadOnlyList<string>? RequiresAbsenceOfUrgencies,
        bool RequiresNoIdentifiedRedFlags);

    private sealed record UrgencyDto(
        string Code,
        int SeverityRank,
        string Description);

    private sealed record DispositionDto(
        string Code,
        string ForUrgency,
        string Recommendation);

    private sealed record RulePackageDto(
        IReadOnlyList<UrgencyDto> Urgencies,
        IReadOnlyList<DispositionDto> Dispositions,
        IReadOnlyList<RedFlagDto> RedFlags,
        IReadOnlyList<RuleDto> Rules,
        IReadOnlyList<string> ClinicalLimitations)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public ClinicalDefinitionPackageProfile Profile { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DemoIntakePackageDto? DemoIntake { get; init; }
    }

    private sealed record DemoIntakePackageDto(
        string PrimarySymptomDisplayLabel,
        string PrimarySymptomQuestionCode,
        string DurationQuestionCode,
        string IntensityQuestionCode,
        string AdditionalSymptomsQuestionCode,
        IReadOnlyList<string> AdditionalSymptomCatalog,
        IReadOnlyList<string> ApplicableAdditionalSymptoms,
        IReadOnlyList<string> RequiredAnswerQuestionCodes,
        IReadOnlyList<string> ProgressionQuestionCodes,
        bool AdditionalSymptomsAllowsEmptySelection);
}
