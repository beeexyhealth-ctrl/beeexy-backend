using System.Text;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Infrastructure.Triage;

public static class SimplifiedDemoDefinitionPackages
{
    public const string VersionIdentifier = "2026.08.22-demo.1";
    public const string SourceReference =
        "Beeexy_Phase_4.5_Confirmed_Demo_Pathways_Simplified_Packages_Prompt.md";
    public const string PrimarySymptomQuestion = "PRIMARY_SYMPTOM";
    public const string DurationQuestion = "DURATION";
    public const string IntensityQuestion = "INTENSITY";
    public const string AdditionalSymptomsQuestion = "ADDITIONAL_SYMPTOMS";

    private static readonly DateTimeOffset ImportedAndActivatedAt =
        new(2026, 8, 22, 4, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<ClinicalDefinitionPackage> CreateAll() =>
    [
        Create(ClinicalPathways.Headache),
        Create(ClinicalPathways.AbdominalPain),
        Create(ClinicalPathways.Fever)
    ];

    public static ClinicalDefinitionPackage Create(ClinicalPathwayCode pathway)
    {
        ArgumentNullException.ThrowIfNull(pathway);
        var specification = GetSpecification(pathway);
        var applicableAdditionalSymptoms = pathway == ClinicalPathways.Fever
            ? new[] { DemoAdditionalSymptoms.NauseaCode, DemoAdditionalSymptoms.DiarrheaCode }
            : DemoAdditionalSymptoms.Catalog.ToArray();
        var questions = CreateQuestions(pathway, specification.DisplayLabel,
            applicableAdditionalSymptoms);
        var definition = CreateDefinition(specification.DisplayLabel,
            applicableAdditionalSymptoms);
        var version = DefinitionVersion.Create(VersionIdentifier);
        var questionInputs = questions.Select(question => new TriageQuestionInput(
            question.Code,
            question.PromptText,
            question.DisplayOrder,
            ClinicalDefinitionSerialization.SerializeQuestion(question),
            null,
            DeterministicId($"{pathway.Value}:question:{question.Code.Value}"))).ToArray();
        var ruleContent = ClinicalDefinitionSerialization.SerializeRulePackage(definition);
        var questionnaire = QuestionnaireDefinitionVersion.Import(
            pathway,
            QuestionnaireCode.Create(specification.QuestionnaireCode),
            version,
            ClinicalDefinitionIntegrity.QuestionnaireHash(questionInputs),
            ClinicalContentStatus.NonClinicalDemo,
            ImportedAndActivatedAt,
            activatedAt: ImportedAndActivatedAt,
            sourceReference: SourceReference,
            id: DeterministicId($"{pathway.Value}:questionnaire"),
            questions: questionInputs);
        var ruleSet = ClinicalRuleSetVersion.Import(
            pathway,
            RuleSetCode.Create(specification.RuleSetCode),
            version,
            ClinicalDefinitionIntegrity.RulePackageHash(ruleContent),
            ClinicalContentStatus.NonClinicalDemo,
            ruleContent,
            ImportedAndActivatedAt,
            activatedAt: ImportedAndActivatedAt,
            sourceReference: SourceReference,
            id: DeterministicId($"{pathway.Value}:rules"));

        return new ClinicalDefinitionPackage(
            pathway,
            questionnaire,
            ruleSet,
            questions,
            [],
            definition);
    }

    private static IReadOnlyList<ClinicalQuestionDefinition> CreateQuestions(
        ClinicalPathwayCode pathway,
        string displayLabel,
        IReadOnlyList<string> applicableAdditionalSymptoms) =>
    [
        Question(
            PrimarySymptomQuestion,
            "What brings you here today?",
            1,
            new ClinicalAnswerDefinition(
                ClinicalAnswerType.SymptomSelection,
                [pathway.Value])),
        Question(
            DurationQuestion,
            $"How long ago did the {displayLabel.ToLowerInvariant()} start?",
            2,
            new ClinicalAnswerDefinition(ClinicalAnswerType.Duration)),
        Question(
            IntensityQuestion,
            "How intense is it from 1 to 10?",
            3,
            new ClinicalAnswerDefinition(
                ClinicalAnswerType.IntegerScale,
                Minimum: 1,
                Maximum: 10)),
        Question(
            AdditionalSymptomsQuestion,
            "Do you have any of these additional symptoms?",
            4,
            new ClinicalAnswerDefinition(
                ClinicalAnswerType.MultipleChoice,
                applicableAdditionalSymptoms))
    ];

    private static ClinicalRulePackageDefinition CreateDefinition(
        string displayLabel,
        IReadOnlyList<string> applicableAdditionalSymptoms)
    {
        var duration = QuestionCode.Create(DurationQuestion);
        var intensity = QuestionCode.Create(IntensityQuestion);
        var additional = QuestionCode.Create(AdditionalSymptomsQuestion);
        return new ClinicalRulePackageDefinition(
            [],
            [],
            [],
            [],
            ["Non-clinical demo intake only; no clinical authority or recommendation."])
        {
            Profile = ClinicalDefinitionPackageProfile.SimplifiedDemoIntake,
            DemoIntake = new DemoIntakePackageDefinition(
                displayLabel,
                QuestionCode.Create(PrimarySymptomQuestion),
                duration,
                intensity,
                additional,
                DemoAdditionalSymptoms.Catalog,
                applicableAdditionalSymptoms,
                [duration, intensity, additional],
                [duration, intensity, additional],
                AdditionalSymptomsAllowsEmptySelection: true)
        };
    }

    private static ClinicalQuestionDefinition Question(
        string code,
        string prompt,
        int displayOrder,
        ClinicalAnswerDefinition answer) => new(
            QuestionCode.Create(code),
            prompt,
            displayOrder,
            answer);

    private static (string DisplayLabel, string QuestionnaireCode, string RuleSetCode)
        GetSpecification(ClinicalPathwayCode pathway)
    {
        if (pathway == ClinicalPathways.Headache)
        {
            return ("Headache", "headache-demo-questionnaire", "headache-demo-neutral-rules");
        }

        if (pathway == ClinicalPathways.AbdominalPain)
        {
            return ("Stomach pain", "abdominal-pain-demo-questionnaire",
                "abdominal-pain-demo-neutral-rules");
        }

        if (pathway == ClinicalPathways.Fever)
        {
            return ("Fever", "fever-demo-questionnaire", "fever-demo-neutral-rules");
        }

        throw new ArgumentOutOfRangeException(
            nameof(pathway), pathway.Value, "The pathway has no simplified demo package.");
    }

    private static EntityId DeterministicId(string seed)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"beeexy:phase4.5:{VersionIdentifier}:{seed}"));
        return EntityId.From(new Guid(bytes.AsSpan(0, 16)));
    }
}
