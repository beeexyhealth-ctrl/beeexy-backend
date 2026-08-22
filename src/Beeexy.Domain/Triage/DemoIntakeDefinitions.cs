namespace Beeexy.Domain.Triage;

public enum DemoAdditionalSymptom
{
    Nausea,
    Diarrhea,
    Fever
}

public static class DemoAdditionalSymptoms
{
    public const string NauseaCode = "NAUSEA";
    public const string DiarrheaCode = "DIARRHEA";
    public const string FeverCode = "FEVER";

    public static IReadOnlyList<string> Catalog { get; } =
        [NauseaCode, DiarrheaCode, FeverCode];

    public static string ToCode(this DemoAdditionalSymptom symptom) => symptom switch
    {
        DemoAdditionalSymptom.Nausea => NauseaCode,
        DemoAdditionalSymptom.Diarrhea => DiarrheaCode,
        DemoAdditionalSymptom.Fever => FeverCode,
        _ => throw new ArgumentOutOfRangeException(nameof(symptom))
    };
}

public sealed record DemoIntakePackageDefinition(
    string PrimarySymptomDisplayLabel,
    QuestionCode PrimarySymptomQuestionCode,
    QuestionCode DurationQuestionCode,
    QuestionCode IntensityQuestionCode,
    QuestionCode AdditionalSymptomsQuestionCode,
    IReadOnlyList<string> AdditionalSymptomCatalog,
    IReadOnlyList<string> ApplicableAdditionalSymptoms,
    IReadOnlyList<QuestionCode> RequiredAnswerQuestionCodes,
    IReadOnlyList<QuestionCode> ProgressionQuestionCodes,
    bool AdditionalSymptomsAllowsEmptySelection);
