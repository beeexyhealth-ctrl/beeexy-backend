namespace Beeexy.Domain.Triage;

public static class ClinicalPathways
{
    public static ClinicalPathwayCode AbdominalPain { get; } =
        ClinicalPathwayCode.Create("ABDOMINAL_PAIN");

    public static ClinicalPathwayCode Headache { get; } =
        ClinicalPathwayCode.Create("HEADACHE");

    public static ClinicalPathwayCode ChestPain { get; } =
        ClinicalPathwayCode.Create("CHEST_PAIN");

    public static ClinicalPathwayCode Fever { get; } =
        ClinicalPathwayCode.Create("FEVER");

    public static ClinicalPathwayCode RespiratorySymptoms { get; } =
        ClinicalPathwayCode.Create("RESPIRATORY_SYMPTOMS");

    public static ClinicalPathwayCode BackPain { get; } =
        ClinicalPathwayCode.Create("BACK_PAIN");

    public static ClinicalPathwayCode OtherSymptoms { get; } =
        ClinicalPathwayCode.Create("OTHER_SYMPTOMS");

    public static IReadOnlyList<ClinicalPathwayCode> Recognized { get; } =
    [
        AbdominalPain,
        Headache,
        ChestPain,
        Fever,
        RespiratorySymptoms,
        BackPain,
        OtherSymptoms
    ];

    public static IReadOnlyList<ClinicalPathwayCode> Supported { get; } =
        [Headache, AbdominalPain, Fever];
}
