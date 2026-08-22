using System.Text;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Infrastructure.Triage;

public static class AbdominalPainProvisionalPackage
{
    public const string VersionIdentifier = "2026.08.21-provisional.1";
    public const string QuestionnaireIdentifier = "abdominal-pain-questionnaire";
    public const string RuleSetIdentifier = "abdominal-pain-rules";
    public const string SourceReference =
        "docs/beeexy-phase4-provisional-clinical-definitions.md";

    private static readonly DateTimeOffset ImportedAndActivatedAt =
        new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);

    public static ClinicalDefinitionPackage Create()
    {
        var questions = CreateQuestions();
        var branches = CreateBranches();
        var ruleDefinitions = CreateRuleDefinitions();
        var version = DefinitionVersion.Create(VersionIdentifier);

        var questionInputs = questions.Select(question => new TriageQuestionInput(
            question.Code,
            question.PromptText,
            question.DisplayOrder,
            ClinicalDefinitionSerialization.SerializeQuestion(question),
            ClinicalDefinitionSerialization.SerializeBranches(
                branches.Where(branch => branch.TriggerQuestionCode == question.Code)),
            DeterministicId($"{VersionIdentifier}:question:{question.Code.Value}"))).ToArray();
        var ruleContent = ClinicalDefinitionSerialization.SerializeRulePackage(ruleDefinitions);

        var questionnaire = QuestionnaireDefinitionVersion.Import(
            ClinicalPathways.AbdominalPain,
            QuestionnaireCode.Create(QuestionnaireIdentifier),
            version,
            ClinicalDefinitionIntegrity.QuestionnaireHash(questionInputs),
            ClinicalContentStatus.ProvisionalReferencePlatformDerived,
            ImportedAndActivatedAt,
            activatedAt: ImportedAndActivatedAt,
            sourceReference: SourceReference,
            id: DeterministicId($"{VersionIdentifier}:questionnaire"),
            questions: questionInputs);
        var ruleSet = ClinicalRuleSetVersion.Import(
            ClinicalPathways.AbdominalPain,
            RuleSetCode.Create(RuleSetIdentifier),
            version,
            ClinicalDefinitionIntegrity.RulePackageHash(ruleContent),
            ClinicalContentStatus.ProvisionalReferencePlatformDerived,
            ruleContent,
            ImportedAndActivatedAt,
            activatedAt: ImportedAndActivatedAt,
            sourceReference: SourceReference,
            id: DeterministicId($"{VersionIdentifier}:rules"));

        return new ClinicalDefinitionPackage(
            ClinicalPathways.AbdominalPain,
            questionnaire,
            ruleSet,
            questions,
            branches,
            ruleDefinitions);
    }

    private static IReadOnlyList<ClinicalQuestionDefinition> CreateQuestions()
    {
        return
        [
            Q("MAIN_SYMPTOM", "What is your main symptom?", 1,
                ClinicalAnswerType.SymptomSelection, ["ABDOMINAL_PAIN"]),
            Q("SYMPTOM_LOCATION", "Where is the symptom located?", 2,
                ClinicalAnswerType.FreeText),
            Q("SYMPTOM_DURATION", "When did it start / how long have you had it?", 3,
                ClinicalAnswerType.Duration),
            Q("PAIN_INTENSITY", "How intense is it on a 0–10 scale?", 4,
                ClinicalAnswerType.IntegerScale, minimum: 0, maximum: 10),
            Q("PAIN_ONSET", "Did it begin suddenly or gradually?", 5,
                ClinicalAnswerType.SingleChoice, ["SUDDEN", "GRADUAL"]),
            Q("PAIN_PROGRESSION",
                "Is it getting better, getting worse, or staying about the same?", 6,
                ClinicalAnswerType.SingleChoice, ["BETTER", "WORSE", "SAME"]),
            Q("PAIN_CHARACTER", "How would you describe the pain?", 7,
                ClinicalAnswerType.FreeText),
            Q("PAIN_PATTERN", "Is it constant or does it come and go?", 8,
                ClinicalAnswerType.SingleChoice, ["CONSTANT", "INTERMITTENT"]),
            Q("AGGRAVATING_FACTORS", "What makes it worse?", 9,
                ClinicalAnswerType.FreeText),
            Q("RELIEVING_FACTORS", "What makes it better?", 10,
                ClinicalAnswerType.FreeText),
            Q("ASSOCIATED_SYMPTOMS",
                "Are you experiencing any other symptoms at the same time?", 11,
                ClinicalAnswerType.MultipleChoice,
                ["FEVER", "VOMITING", "BLOOD", "URINARY_SYMPTOMS", "SHORTNESS_OF_BREATH"]),
            Q("HAS_RELEVANT_MEDICAL_HISTORY",
                "Do you have relevant chronic conditions or major medical history?", 12,
                ClinicalAnswerType.Boolean),
            Q("RELEVANT_MEDICAL_HISTORY", "Which relevant conditions do you have?", 13,
                ClinicalAnswerType.FreeText),
            Q("TAKES_MEDICATIONS_SUPPLEMENTS",
                "Are you currently taking any medications or supplements?", 14,
                ClinicalAnswerType.Boolean),
            Q("MEDICATIONS_SUPPLEMENTS",
                "Which medications or supplements are you taking?", 15,
                ClinicalAnswerType.FreeText),
            Q("HAS_ALLERGIES", "Do you have any allergies?", 16,
                ClinicalAnswerType.Boolean),
            Q("ALLERGENS", "What are you allergic to?", 17,
                ClinicalAnswerType.FreeText),
            Q("ABDOMINAL_LOCATION", "Where specifically in the abdomen is the pain?", 18,
                ClinicalAnswerType.FreeText,
                priority: ClinicalQuestionPriority.HigherPriorityClarification),
            Q("LOWER_ABDOMINAL_SIDE",
                "Is the lower abdominal pain predominantly right-sided, left-sided, or central?",
                19,
                ClinicalAnswerType.SingleChoice,
                ["RIGHT", "LEFT", "CENTRAL"],
                priority: ClinicalQuestionPriority.HigherPriorityClarification),
            Q("URINARY_REPRODUCTIVE_SYMPTOMS",
                "Are there relevant urinary or reproductive symptoms?", 20,
                ClinicalAnswerType.FreeText),
            Q("HAS_FEVER", "Do you have a fever?", 21,
                ClinicalAnswerType.Boolean,
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("MEASURED_TEMPERATURE_C",
                "What is the measured temperature, if known?", 22,
                ClinicalAnswerType.Temperature,
                unit: "CELSIUS",
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("FEVER_ONSET", "When did the fever begin?", 23,
                ClinicalAnswerType.Duration),
            Q("ABDOMINAL_GI_SYMPTOMS",
                "Which gastrointestinal symptoms are associated with the abdominal pain?", 24,
                ClinicalAnswerType.MultipleChoice,
                [
                    "NAUSEA",
                    "VOMITING",
                    "DIARRHEA",
                    "CONSTIPATION",
                    "BLOOD_IN_STOOL",
                    "BLOOD_IN_URINE"
                ]),
            Q("HAS_VOMITING", "Are you vomiting?", 25,
                ClinicalAnswerType.Boolean),
            Q("VOMITING_PERSISTENT", "Is the vomiting persistent?", 26,
                ClinicalAnswerType.Boolean,
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("CAN_KEEP_FLUIDS_DOWN", "Can you keep fluids down?", 27,
                ClinicalAnswerType.Boolean,
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("HAS_BLOOD", "Have you noticed blood?", 28,
                ClinicalAnswerType.Boolean,
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("BLOOD_SOURCE", "Is the blood in vomit, stool, or urine?", 29,
                ClinicalAnswerType.MultipleChoice, ["VOMIT", "STOOL", "URINE"],
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("BLOOD_IN_VOMIT", "Is there blood in the vomit?", 30,
                ClinicalAnswerType.Boolean,
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("BLOOD_IN_STOOL", "Is there blood in the stool?", 31,
                ClinicalAnswerType.Boolean,
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("BLACK_TARRY_STOOL", "Is the stool black or tarry?", 32,
                ClinicalAnswerType.Boolean,
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("BLOOD_IN_URINE", "Is there blood in the urine?", 33,
                ClinicalAnswerType.Boolean,
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("HAS_URINARY_SYMPTOMS", "Are you experiencing urinary symptoms?", 34,
                ClinicalAnswerType.Boolean),
            Q("PAINFUL_URINATION", "Is urination painful or burning?", 35,
                ClinicalAnswerType.Boolean,
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("URINARY_URGENCY_FREQUENCY",
                "Has urinary urgency or frequency increased?", 36,
                ClinicalAnswerType.Boolean),
            Q("PAIN_RADIATION",
                "Does the abdominal pain radiate to the back, chest, shoulder, flank, or groin?",
                37,
                ClinicalAnswerType.MultipleChoice,
                ["BACK", "CHEST", "SHOULDER", "FLANK", "GROIN"],
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("SHORTNESS_OF_BREATH",
                "Is the abdominal pain accompanied by shortness of breath?", 38,
                ClinicalAnswerType.Boolean,
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("PAIN_RAPIDLY_WORSENING", "Is the abdominal pain rapidly worsening?", 39,
                ClinicalAnswerType.Boolean,
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("PAIN_BECAME_SHARP_OR_MORE_INTENSE",
                "Has the pain become sharp or significantly more intense?", 40,
                ClinicalAnswerType.Boolean,
                priority: ClinicalQuestionPriority.RedFlagScreening),
            Q("CAN_EAT_DRINK", "Are you able to eat and drink?", 41,
                ClinicalAnswerType.Boolean)
        ];
    }

    private static IReadOnlyList<ClinicalBranchDefinition> CreateBranches()
    {
        var redFlagQuestions = Codes(
            "HAS_FEVER", "VOMITING_PERSISTENT", "CAN_KEEP_FLUIDS_DOWN", "HAS_BLOOD",
            "BLACK_TARRY_STOOL", "PAIN_RADIATION", "SHORTNESS_OF_BREATH",
            "PAINFUL_URINATION", "PAIN_RAPIDLY_WORSENING",
            "PAIN_BECAME_SHARP_OR_MORE_INTENSE");
        return
        [
            B("PAIN_DETAILS", "MAIN_SYMPTOM", ClinicalConditionOperator.Equals,
                ["ABDOMINAL_PAIN"],
                Codes("SYMPTOM_LOCATION", "PAIN_INTENSITY", "PAIN_CHARACTER", "PAIN_ONSET",
                    "PAIN_PROGRESSION", "SYMPTOM_DURATION", "PAIN_PATTERN")),
            B("SUDDEN_ONSET_PRIORITY", "PAIN_ONSET", ClinicalConditionOperator.Equals,
                ["SUDDEN"],
                Codes("PAIN_INTENSITY", "PAIN_PROGRESSION").Concat(redFlagQuestions).ToArray(),
                ClinicalQuestionPriority.RedFlagScreening),
            B("WORSENING_PRIORITY", "PAIN_PROGRESSION", ClinicalConditionOperator.Equals,
                ["WORSE"], redFlagQuestions, ClinicalQuestionPriority.RedFlagScreening),
            B("HIGH_INTENSITY_PRIORITY", "PAIN_INTENSITY",
                ClinicalConditionOperator.ClassifiedAs, ["HIGH_INTENSITY"], redFlagQuestions,
                ClinicalQuestionPriority.RedFlagScreening),
            B("ABDOMINAL_DETAILS", "MAIN_SYMPTOM", ClinicalConditionOperator.Equals,
                ["ABDOMINAL_PAIN"], Codes("ABDOMINAL_LOCATION", "ABDOMINAL_GI_SYMPTOMS")),
            B("LOWER_ABDOMINAL_DETAILS", "ABDOMINAL_LOCATION",
                ClinicalConditionOperator.ClassifiedAs, ["LOWER_ABDOMEN"],
                Codes("LOWER_ABDOMINAL_SIDE", "URINARY_REPRODUCTIVE_SYMPTOMS",
                    "HAS_URINARY_SYMPTOMS"),
                ClinicalQuestionPriority.HigherPriorityClarification),
            B("FEVER_DETAILS", "HAS_FEVER", ClinicalConditionOperator.Equals,
                ["TRUE"], Codes("MEASURED_TEMPERATURE_C", "FEVER_ONSET"),
                ClinicalQuestionPriority.RedFlagScreening),
            B("GI_ASSOCIATED_DETAILS", "MAIN_SYMPTOM", ClinicalConditionOperator.Equals,
                ["ABDOMINAL_PAIN"],
                Codes("ABDOMINAL_GI_SYMPTOMS", "HAS_VOMITING", "HAS_BLOOD")),
            B("VOMITING_DETAILS", "HAS_VOMITING", ClinicalConditionOperator.Equals,
                ["TRUE"],
                Codes("VOMITING_PERSISTENT", "CAN_KEEP_FLUIDS_DOWN", "BLOOD_IN_VOMIT"),
                ClinicalQuestionPriority.RedFlagScreening),
            B("BLOOD_SOURCE_DETAILS", "HAS_BLOOD", ClinicalConditionOperator.Equals,
                ["TRUE"],
                Codes("BLOOD_SOURCE", "BLOOD_IN_VOMIT", "BLOOD_IN_STOOL", "BLOOD_IN_URINE"),
                ClinicalQuestionPriority.RedFlagScreening),
            B("URINARY_DETAILS", "HAS_URINARY_SYMPTOMS", ClinicalConditionOperator.Equals,
                ["TRUE"], Codes("PAINFUL_URINATION", "URINARY_URGENCY_FREQUENCY"),
                ClinicalQuestionPriority.HigherPriorityClarification),
            B("MEDICAL_HISTORY_DETAILS", "HAS_RELEVANT_MEDICAL_HISTORY",
                ClinicalConditionOperator.Equals, ["TRUE"], Codes("RELEVANT_MEDICAL_HISTORY")),
            B("MEDICATION_DETAILS", "TAKES_MEDICATIONS_SUPPLEMENTS",
                ClinicalConditionOperator.Equals, ["TRUE"], Codes("MEDICATIONS_SUPPLEMENTS")),
            B("ALLERGY_DETAILS", "HAS_ALLERGIES", ClinicalConditionOperator.Equals,
                ["TRUE"], Codes("ALLERGENS"))
        ];
    }

    private static ClinicalRulePackageDefinition CreateRuleDefinitions()
    {
        var abdominalPain = C("MAIN_SYMPTOM", ClinicalConditionOperator.Equals, "ABDOMINAL_PAIN");
        var urgencies = new[]
        {
            U(ClinicalUrgencies.VeryLow, 0,
                "Non-urgent situations with no identified immediate life-threatening risk and which do not appear to require immediate medical attention."),
            U(ClinicalUrgencies.Low, 1,
                "Non-urgent situations without apparent immediate life-threatening risk, but which could persist or become complicated and may require medical consultation."),
            U(ClinicalUrgencies.Medium, 2,
                "Situations that require prompt medical evaluation and could become serious if they are not assessed."),
            U(ClinicalUrgencies.High, 3,
                "Emergency or very urgent situations with foreseeable serious risk where the time to medical assistance matters."),
            U(ClinicalUrgencies.Critical, 4,
                "Emergency situations involving an immediate threat to life and requiring immediate emergency attention.")
        };
        var dispositions = new[]
        {
            D("VERY_LOW_MONITOR_AND_ESCALATE_IF_WORSENING", ClinicalUrgencies.VeryLow,
                "No immediate warning signs requiring urgent medical attention were identified from the answers provided. Continue monitoring the symptoms and seek medical care if they worsen, persist, or new warning signs appear."),
            D("LOW_CONSULT_IF_PERSISTING_OR_WORSENING", ClinicalUrgencies.Low,
                "Immediate emergency care does not appear to be required based on the information provided, but medical consultation is recommended if the symptoms persist, worsen, or new concerning symptoms appear."),
            D("MEDIUM_PROMPT_MEDICAL_EVALUATION", ClinicalUrgencies.Medium,
                "A prompt medical evaluation is recommended. Beeexy may help the patient find an appropriate healthcare professional or service."),
            D("HIGH_URGENT_EMERGENCY_EVALUATION", ClinicalUrgencies.High,
                "Go to an emergency department or hospital as soon as possible. The symptoms reported may require urgent medical evaluation."),
            D("CRITICAL_IMMEDIATE_EMERGENCY_ATTENTION", ClinicalUrgencies.Critical,
                "Seek emergency medical attention immediately.")
        };

        var redFlags = new[]
        {
            R("SUDDEN_SEVERE_ABDOMINAL_PAIN", "Sudden severe abdominal pain.",
                abdominalPain,
                C("PAIN_ONSET", ClinicalConditionOperator.Equals, "SUDDEN"),
                C("PAIN_INTENSITY", ClinicalConditionOperator.ClassifiedAs, "SEVERE")),
            R("RAPIDLY_WORSENING_ABDOMINAL_PAIN",
                "Abdominal pain that is rapidly worsening.", abdominalPain,
                C("PAIN_RAPIDLY_WORSENING", ClinicalConditionOperator.Equals, "TRUE")),
            R("SHARP_OR_SIGNIFICANTLY_MORE_INTENSE_PAIN",
                "Pain that becomes sharp or significantly more intense.", abdominalPain,
                C("PAIN_BECAME_SHARP_OR_MORE_INTENSE", ClinicalConditionOperator.Equals, "TRUE")),
            R("ABDOMINAL_PAIN_WITH_TEMPERATURE_AT_LEAST_38_C",
                "Fever of 38 °C or higher associated with abdominal pain.", abdominalPain,
                C("MEASURED_TEMPERATURE_C", ClinicalConditionOperator.GreaterThanOrEqual, "38")),
            R("PERSISTENT_VOMITING", "Persistent vomiting.",
                C("VOMITING_PERSISTENT", ClinicalConditionOperator.Equals, "TRUE")),
            R("INABILITY_TO_KEEP_FLUIDS_DOWN", "Inability to keep fluids down.",
                C("CAN_KEEP_FLUIDS_DOWN", ClinicalConditionOperator.Equals, "FALSE")),
            R("BLOOD_IN_VOMIT", "Blood in vomit.",
                C("BLOOD_IN_VOMIT", ClinicalConditionOperator.Equals, "TRUE")),
            R("BLOOD_IN_STOOL", "Blood in stool.",
                C("BLOOD_IN_STOOL", ClinicalConditionOperator.Equals, "TRUE")),
            R("BLACK_TARRY_STOOL", "Black or tarry stool.",
                C("BLACK_TARRY_STOOL", ClinicalConditionOperator.Equals, "TRUE")),
            new ClinicalRedFlagDefinition(
                "PAIN_RADIATING_TO_BACK_CHEST_OR_SHOULDER",
                "Abdominal pain radiating to the back, chest, or shoulder.",
                [abdominalPain],
                [
                    C("PAIN_RADIATION", ClinicalConditionOperator.ContainsAny, "BACK"),
                    C("PAIN_RADIATION", ClinicalConditionOperator.ContainsAny, "CHEST"),
                    C("PAIN_RADIATION", ClinicalConditionOperator.ContainsAny, "SHOULDER")
                ]),
            R("ABDOMINAL_PAIN_WITH_SHORTNESS_OF_BREATH",
                "Abdominal pain accompanied by shortness of breath.", abdominalPain,
                C("SHORTNESS_OF_BREATH", ClinicalConditionOperator.Equals, "TRUE")),
            new ClinicalRedFlagDefinition(
                "FLANK_OR_GROIN_RADIATION_WITH_URINARY_SYMPTOMS",
                "Pain radiating to the flank or groin with urinary symptoms.",
                [
                    abdominalPain,
                    C("HAS_URINARY_SYMPTOMS", ClinicalConditionOperator.Equals, "TRUE")
                ],
                [
                    C("PAIN_RADIATION", ClinicalConditionOperator.ContainsAny, "FLANK"),
                    C("PAIN_RADIATION", ClinicalConditionOperator.ContainsAny, "GROIN")
                ]),
            R("PAINFUL_URINATION_REQUIRES_EVALUATION",
                "Painful urination requires additional evaluation.", abdominalPain,
                C("PAINFUL_URINATION", ClinicalConditionOperator.Equals, "TRUE"))
        };

        var rules = new[]
        {
            Rule("HIGH_SUDDEN_SEVERE_ABDOMINAL_PAIN", ClinicalUrgencies.High, true,
                "Sudden severe abdominal pain establishes HIGH minimum urgency.",
                [
                    abdominalPain,
                    C("PAIN_ONSET", ClinicalConditionOperator.Equals, "SUDDEN"),
                    C("PAIN_INTENSITY", ClinicalConditionOperator.ClassifiedAs, "SEVERE")
                ]),
            Rule("HIGH_RAPIDLY_WORSENING_OR_SHARPLY_INTENSIFYING_PAIN",
                ClinicalUrgencies.High, true,
                "Rapidly worsening or sharply intensifying abdominal pain establishes HIGH minimum urgency.",
                [abdominalPain],
                [
                    C("PAIN_RAPIDLY_WORSENING", ClinicalConditionOperator.Equals, "TRUE"),
                    C("PAIN_BECAME_SHARP_OR_MORE_INTENSE",
                        ClinicalConditionOperator.Equals, "TRUE")
                ]),
            Rule("HIGH_BLOOD_IN_VOMIT_STOOL_OR_BLACK_TARRY_STOOL",
                ClinicalUrgencies.High, true,
                "Blood in vomit or stool, or black/tarry stool, establishes HIGH minimum urgency.",
                [abdominalPain],
                [
                    C("BLOOD_IN_VOMIT", ClinicalConditionOperator.Equals, "TRUE"),
                    C("BLOOD_IN_STOOL", ClinicalConditionOperator.Equals, "TRUE"),
                    C("BLACK_TARRY_STOOL", ClinicalConditionOperator.Equals, "TRUE")
                ]),
            Rule("HIGH_ABDOMINAL_PAIN_WITH_SHORTNESS_OF_BREATH",
                ClinicalUrgencies.High, true,
                "Abdominal pain with shortness of breath establishes HIGH minimum urgency.",
                [
                    abdominalPain,
                    C("SHORTNESS_OF_BREATH", ClinicalConditionOperator.Equals, "TRUE")
                ]),
            Rule("MEDIUM_ABDOMINAL_PAIN_WITH_TEMPERATURE_AT_LEAST_38_C",
                ClinicalUrgencies.Medium, true,
                "Abdominal pain with temperature at least 38 °C establishes at least MEDIUM urgency.",
                [
                    abdominalPain,
                    C("MEASURED_TEMPERATURE_C",
                        ClinicalConditionOperator.GreaterThanOrEqual, "38")
                ]),
            Rule("MEDIUM_PERSISTENT_VOMITING", ClinicalUrgencies.Medium, true,
                "Persistent vomiting establishes at least MEDIUM urgency.",
                [C("VOMITING_PERSISTENT", ClinicalConditionOperator.Equals, "TRUE")]),
            Rule("MEDIUM_INABILITY_TO_KEEP_FLUIDS_DOWN", ClinicalUrgencies.Medium, true,
                "Inability to keep fluids down establishes at least MEDIUM urgency.",
                [C("CAN_KEEP_FLUIDS_DOWN", ClinicalConditionOperator.Equals, "FALSE")]),
            Rule("MEDIUM_CONCERNING_URINARY_SYMPTOMS", ClinicalUrgencies.Medium, true,
                "Abdominal pain with concerning urinary symptoms establishes at least MEDIUM urgency.",
                [
                    abdominalPain,
                    C("HAS_URINARY_SYMPTOMS",
                        ClinicalConditionOperator.ClassifiedAs, "CONCERNING")
                ]),
            Rule("LOW_PERSISTING_WITHOUT_IMPROVEMENT", ClinicalUrgencies.Low, false,
                "Abdominal pain persisting over 24 hours without improvement and without HIGH or CRITICAL red flags establishes LOW urgency.",
                [
                    abdominalPain,
                    C("SYMPTOM_DURATION", ClinicalConditionOperator.GreaterThan, "24_HOURS"),
                    C("PAIN_PROGRESSION", ClinicalConditionOperator.ClassifiedAs, "NO_IMPROVEMENT")
                ],
                requiresAbsence: [ClinicalUrgencies.High, ClinicalUrgencies.Critical]),
            Rule("VERY_LOW_STABLE_OR_IMPROVING_WITH_ORAL_INTAKE",
                ClinicalUrgencies.VeryLow, false,
                "Mild-to-moderate, stable or improving symptoms with oral intake and no red flags establish VERY_LOW urgency.",
                [
                    abdominalPain,
                    C("PAIN_INTENSITY", ClinicalConditionOperator.ClassifiedAs,
                        "MILD_TO_MODERATE"),
                    C("PAIN_PROGRESSION", ClinicalConditionOperator.ClassifiedAs,
                        "STABLE_OR_IMPROVING"),
                    C("CAN_EAT_DRINK", ClinicalConditionOperator.Equals, "TRUE")
                ],
                requiresNoRedFlags: true)
        };

        return new ClinicalRulePackageDefinition(
            urgencies,
            dispositions,
            redFlags,
            rules,
            [
                "The package is reference-platform-derived, provisional, and pending formal clinical review.",
                "The current abdominal-pain reference material does not define a complete exhaustive set of CRITICAL triggers; no CRITICAL trigger is included in this version.",
                "Red flags are findings or criteria, not diagnoses.",
                "Urgency and disposition/recommendation remain separate concepts."
            ]);
    }

    private static ClinicalQuestionDefinition Q(
        string code,
        string prompt,
        int order,
        ClinicalAnswerType type,
        IReadOnlyList<string>? values = null,
        decimal? minimum = null,
        decimal? maximum = null,
        string? unit = null,
        ClinicalQuestionPriority priority = ClinicalQuestionPriority.Ordinary)
    {
        return new ClinicalQuestionDefinition(
            QuestionCode.Create(code),
            prompt,
            order,
            new ClinicalAnswerDefinition(type, values, minimum, maximum, unit),
            priority);
    }

    private static ClinicalBranchDefinition B(
        string code,
        string trigger,
        ClinicalConditionOperator @operator,
        IReadOnlyList<string> expected,
        IReadOnlyList<QuestionCode> next,
        ClinicalQuestionPriority priority = ClinicalQuestionPriority.Ordinary)
    {
        return new ClinicalBranchDefinition(
            code,
            QuestionCode.Create(trigger),
            @operator,
            expected,
            next,
            priority);
    }

    private static ClinicalConditionDefinition C(
        string fact,
        ClinicalConditionOperator @operator,
        string expected)
    {
        return new ClinicalConditionDefinition(QuestionCode.Create(fact), @operator, expected);
    }

    private static ClinicalRedFlagDefinition R(
        string code,
        string description,
        params ClinicalConditionDefinition[] allOf)
    {
        return new ClinicalRedFlagDefinition(code, description, allOf);
    }

    private static ClinicalRuleDefinition Rule(
        string code,
        UrgencyCode urgency,
        bool isRedFlag,
        string description,
        IReadOnlyList<ClinicalConditionDefinition> allOf,
        IReadOnlyList<ClinicalConditionDefinition>? anyOf = null,
        IReadOnlyList<UrgencyCode>? requiresAbsence = null,
        bool requiresNoRedFlags = false)
    {
        return new ClinicalRuleDefinition(
            code,
            urgency,
            isRedFlag,
            description,
            allOf,
            anyOf,
            requiresAbsence,
            requiresNoRedFlags);
    }

    private static UrgencyDefinition U(
        UrgencyCode code,
        int rank,
        string description)
    {
        return new UrgencyDefinition(code, rank, description);
    }

    private static DispositionDefinition D(
        string code,
        UrgencyCode urgency,
        string recommendation)
    {
        return new DispositionDefinition(code, urgency, recommendation);
    }

    private static QuestionCode[] Codes(params string[] values)
    {
        return values.Select(QuestionCode.Create).ToArray();
    }

    private static EntityId DeterministicId(string seed)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"beeexy:phase4.2:{seed}"));
        return EntityId.From(new Guid(bytes.AsSpan(0, 16)));
    }
}
