using System.Text.Json;
using Beeexy.Application.Care;
using Beeexy.Domain.Care;
using Beeexy.Domain.Triage;

namespace Beeexy.Infrastructure.Care;

public static class AndreaSymptomDiaryPackages
{
    public const string VersionIdentifier = "andrea-symptoms-v1";
    public const string SourceReference = "docs/symptoms.md";
    public const string SourceSha256 =
        "dd851aa5e1ee2daac98926314b287e713d2e6c13c6f646ad4f1fc0e260907a62";
    public const string ChestPainContentSha256 =
        "56bd1da0a9e50de50ebf63a256beb2c67773b2eeeb75b3cdc9d4659c1a9d5101";
    public const string AbdominalPainContentSha256 =
        "78537e6f95cb1a3ee1bbe91f67361d5e7a5c140c28be0eb916dfe58cf9110418";
    public const string FeverContentSha256 =
        "1ba55348de268ace25b4d415ca6aacc1078b44a62900ac50edb0b94d4e36bb84";
    public const string HeadacheContentSha256 =
        "acdd3489902a53e411723097fd14844c91600484cd88a0ca0f2f06c2e18267ed";

    private const string TextAnswerSchema = "{\"type\":\"string\"}";

    private static readonly DateTimeOffset ImportedAt =
        new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset ApprovalEffectiveAt { get; } =
        new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

    private static readonly ClinicalContentStatus ApprovedMedicalTeamContent =
        ClinicalContentStatus.MedicalTeamApproved;

    public static IReadOnlyList<SymptomDiaryPackageDefinition> CreateAll() =>
    [
        CreateChestPain(),
        CreateAbdominalPain(),
        CreateFever(),
        CreateHeadache()
    ];

    public static SymptomDiaryPackageDefinition Create(ClinicalPathwayCode pathway)
    {
        ArgumentNullException.ThrowIfNull(pathway);
        return CreateAll().SingleOrDefault(package => package.Pathway == pathway) ??
            throw new ArgumentOutOfRangeException(
                nameof(pathway),
                pathway.Value,
                "Andrea's symptom source has no package for the pathway.");
    }

    private static SymptomDiaryPackageDefinition CreateChestPain() => Package(
        "chest-pain",
        ClinicalPathways.ChestPain,
        [
            Question("onset", "When did it start?", 1),
            Question("severity", "How severe is it?", 2),
            Question(
                "character",
                "What does it feel like?",
                3,
                [
                    Option("pressure-tightness", "Pressure/tightness", 1),
                    Option("burning", "Burning", 2),
                    Option("sharp-stabbing", "Sharp/stabbing", 3),
                    Option("tearing", "Tearing", 4),
                    Option("other-not-sure", "Other / not sure", 5)
                ]),
            Question(
                "associated-symptoms",
                "Do you have any of these with it?",
                4,
                [
                    Option("shortness-of-breath", "Shortness of breath", 1),
                    Option("sweating", "Sweating", 2),
                    Option("nausea-vomiting", "Nausea/vomiting", 3),
                    Option("palpitations", "Palpitations", 4),
                    Option("dizziness-fainting", "Dizziness/fainting", 5),
                    Option(
                        "pain-spreading-arm-jaw-shoulder-back",
                        "Pain spreading to arm, jaw, shoulder or back",
                        6),
                    Option("none", "None of these", 7)
                ],
                multiple: true)
        ],
        Warnings(
            "severe/new chest pain",
            "pressure/tightness",
            "radiation to arm/jaw/back",
            "shortness of breath",
            "syncope",
            "marked sweating",
            "sudden tearing pain",
            "neurological symptoms."));

    private static SymptomDiaryPackageDefinition CreateAbdominalPain() => Package(
        "abdominal-pain",
        ClinicalPathways.AbdominalPain,
        [
            Question("onset", "When did it start?", 1),
            Question(
                "location",
                "Where is the pain?",
                2,
                [
                    Option("upper-abdomen", "Upper abdomen", 1),
                    Option("lower-abdomen", "Lower abdomen", 2),
                    Option("right-side", "Right side", 3),
                    Option("left-side", "Left side", 4),
                    Option("around-navel", "Around the navel", 5),
                    Option("diffuse-all-over", "Diffuse/all over", 6),
                    Option("other-not-sure", "Other / not sure", 7)
                ]),
            Question("severity", "How severe is it?", 3),
            Question(
                "associated-symptoms",
                "Which symptoms do you have?",
                4,
                [
                    Option("vomiting", "Vomiting", 1),
                    Option("diarrhea", "Diarrhea", 2),
                    Option("constipation", "Constipation/no bowel movements", 3),
                    Option("abdominal-swelling", "Abdominal swelling", 4),
                    Option("fever", "Fever", 5),
                    Option("blood-stool-vomit", "Blood in stool or vomit", 6),
                    Option("urinary-symptoms", "Urinary symptoms", 7),
                    Option(
                        "vaginal-bleeding-pregnancy-possibility",
                        "Vaginal bleeding/pregnancy possibility",
                        8),
                    Option("none", "None of these", 9)
                ],
                multiple: true)
        ],
        Warnings(
            "sudden/severe pain",
            "rapidly worsening pain",
            "rigid abdomen",
            "persistent vomiting",
            "gastrointestinal bleeding",
            "syncope",
            "hypotension",
            "marked distension",
            "significant pain with possible pregnancy."));

    private static SymptomDiaryPackageDefinition CreateFever() => Package(
        "fever",
        ClinicalPathways.Fever,
        [
            Question("onset", "When did the fever start?", 1),
            Question(
                "highest-temperature",
                "What is the highest temperature you've measured?",
                2,
                [
                    Option("below-38-c", "<38.0°C", 1),
                    Option("38-to-38-9-c", "38.0–38.9°C", 2),
                    Option("39-to-39-9-c", "39.0–39.9°C", 3),
                    Option("at-least-40-c", "≥40.0°C", 4),
                    Option("not-measured", "I haven't measured it", 5)
                ]),
            Question(
                "associated-symptoms",
                "Which other symptoms do you have?",
                3,
                [
                    Option("cough-shortness-of-breath", "Cough/shortness of breath", 1),
                    Option("sore-throat-ent", "Sore throat/ENT symptoms", 2),
                    Option("abdominal-symptoms", "Abdominal symptoms", 3),
                    Option("urinary-symptoms", "Urinary symptoms", 4),
                    Option("skin-rash", "Skin rash", 5),
                    Option(
                        "severe-headache-neck-stiffness",
                        "Severe headache/neck stiffness",
                        6),
                    Option("none", "None of these", 7)
                ],
                multiple: true),
            Question(
                "overall-feeling",
                "How do you feel overall?",
                4,
                [
                    Option("generally-well", "Generally well", 1),
                    Option(
                        "unwell-functioning-normally",
                        "Unwell but functioning normally",
                        2),
                    Option(
                        "very-unwell-unable-function-normally",
                        "Very unwell / unable to function normally",
                        3)
                ])
        ],
        Warnings(
            "confusion/altered consciousness",
            "difficulty breathing",
            "fainting/hypotension",
            "seizure",
            "neck rigidity",
            "non-blanching/purpuric rash",
            "severe dehydration",
            "rapidly deteriorating condition",
            "significant immunosuppression."));

    private static SymptomDiaryPackageDefinition CreateHeadache() => Package(
        "headache",
        ClinicalPathways.Headache,
        [
            Question("onset", "When did it start?", 1),
            Question(
                "maximum-intensity-onset",
                "How quickly did it reach maximum intensity?",
                2,
                [
                    Option(
                        "immediately-seconds-minutes",
                        "Immediately / within seconds–minutes",
                        1),
                    Option("within-several-hours", "Within several hours", 2),
                    Option("gradually-over-one-day", "Gradually over >1 day", 3),
                    Option("not-sure", "Not sure", 4)
                ]),
            Question("severity", "How severe is it?", 3),
            Question(
                "associated-symptoms",
                "Do you have any of these symptoms?",
                4,
                [
                    Option("fever-neck-stiffness", "Fever/neck stiffness", 1),
                    Option("weakness-numbness", "Weakness or numbness", 2),
                    Option("difficulty-speaking", "Difficulty speaking", 3),
                    Option(
                        "visual-loss-disturbance",
                        "Visual loss/new visual disturbance",
                        4),
                    Option(
                        "confusion-fainting-seizure",
                        "Confusion/fainting/seizure",
                        5),
                    Option("repeated-vomiting", "Repeated vomiting", 6),
                    Option(
                        "recent-significant-head-trauma",
                        "Recent significant head trauma",
                        7),
                    Option("none", "None of these", 8)
                ],
                multiple: true)
        ],
        Warnings(
            "thunderclap onset",
            "neurological deficit",
            "altered consciousness",
            "seizure",
            "fever + neck stiffness",
            "acute visual loss",
            "significant head trauma",
            "pregnancy/postpartum",
            "new/progressive headache substantially different from usual."));

    private static SymptomDiaryPackageDefinition Package(
        string pathwaySlug,
        ClinicalPathwayCode pathway,
        IReadOnlyList<SymptomDiaryQuestionDefinition> questions,
        IReadOnlyList<SymptomWarningSignDefinition> warnings) => new(
            SymptomDiaryCode.Create($"andrea-{pathwaySlug}-symptom-diary"),
            DefinitionVersion.Create(VersionIdentifier),
            SymptomDiaryCode.Create($"andrea-{pathwaySlug}-questions"),
            DefinitionVersion.Create(VersionIdentifier),
            SymptomDiaryCode.Create($"andrea-{pathwaySlug}-warning-information"),
            DefinitionVersion.Create(VersionIdentifier),
            pathway,
            ApprovedMedicalTeamContent,
            SourceReference,
            ImportedAt,
            ApprovedAt: ApprovalEffectiveAt,
            ActivatedAt: ApprovalEffectiveAt,
            InformationalHeading: "Red flags:",
            InformationalBody: null,
            questions,
            warnings,
            SymptomDiarySha256.FromHash(ExpectedContentHash(pathway)));

    private static string ExpectedContentHash(ClinicalPathwayCode pathway)
    {
        if (pathway == ClinicalPathways.ChestPain)
        {
            return ChestPainContentSha256;
        }

        if (pathway == ClinicalPathways.AbdominalPain)
        {
            return AbdominalPainContentSha256;
        }

        if (pathway == ClinicalPathways.Fever)
        {
            return FeverContentSha256;
        }

        if (pathway == ClinicalPathways.Headache)
        {
            return HeadacheContentSha256;
        }

        throw new ArgumentOutOfRangeException(nameof(pathway));
    }

    private static SymptomDiaryQuestionDefinition Question(
        string code,
        string prompt,
        int order,
        IReadOnlyList<SymptomDiaryQuestionOptionDefinition>? options = null,
        bool multiple = false) => new(
            SymptomDiaryCode.Create(code),
            prompt,
            order,
            AnswerSchema(options ?? [], multiple),
            IsRequired: false,
            options ?? []);

    private static string AnswerSchema(
        IReadOnlyList<SymptomDiaryQuestionOptionDefinition> options,
        bool multiple)
    {
        if (options.Count == 0)
        {
            return TextAnswerSchema;
        }

        var values = string.Join(",", options.Select(option =>
            JsonSerializer.Serialize(option.Value)));
        return multiple
            ? $"{{\"type\":\"array\",\"items\":{{\"type\":\"string\"," +
                $"\"enum\":[{values}]}},\"uniqueItems\":true}}"
            : $"{{\"type\":\"string\",\"enum\":[{values}]}}";
    }

    private static SymptomDiaryQuestionOptionDefinition Option(
        string code,
        string exactText,
        int order) => new(
            SymptomDiaryCode.Create(code),
            exactText,
            exactText,
            order);

    private static IReadOnlyList<SymptomWarningSignDefinition> Warnings(
        params string[] exactText) => exactText.Select((value, index) =>
            new SymptomWarningSignDefinition(
                SymptomDiaryCode.Create($"warning-{index + 1:00}"),
                value,
                index + 1)).ToArray();
}
