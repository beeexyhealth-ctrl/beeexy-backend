using System.Text.Json;
using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;

namespace Beeexy.Tests.Unit.Ai;

[Trait("Category", "Phase106")]
[Trait("Category", "Phase108")]
public sealed class SecondOpinionPromptSafetyTests
{
    private readonly SecondOpinionResultSchemaV1 schema = new();
    private readonly BeeexyAiSafetyValidator safety =
        new(AiSafetyProductContent.Current);

    [Fact]
    public void Contract_HasDistinctStablePromptResultAndDisclaimerVersions()
    {
        Assert.Equal("ai-second-opinion@v1", SecondOpinionContract.Prompt.PersistenceValue);
        Assert.Equal("ai-second-opinion-result", SecondOpinionContract.Result.SchemaIdentifier);
        Assert.Equal("v1", SecondOpinionContract.Result.Version);
        Assert.Equal("ai-second-opinion-result@v1", SecondOpinionProductContent.ResultVersion);
        Assert.Equal("ai-second-opinion-disclaimer-v1",
            SecondOpinionProductContent.DisclaimerVersion);
    }

    [Fact]
    public void ProductDisclaimer_IsExactCentralizedApprovedContent()
    {
        Assert.Equal(
            "This is not a medical diagnosis. Beeexy AI offers educational insights based " +
            "on clinical literature, not a substitute for a licensed physician. Always " +
            "discuss results with your doctor.",
            SecondOpinionProductContent.Disclaimer);
        Assert.DoesNotContain("provider", SecondOpinionProductContent.Disclaimer,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prompt_ContainsAllProductBoundariesAndNoRuntimeInputInInstructions()
    {
        const string privateInput = "private-medical-input-marker";
        var resolved = new SecondOpinionPromptV1().Build(privateInput);

        Assert.Equal(privateInput, resolved.UserContent);
        Assert.DoesNotContain(privateInput, resolved.SystemInstructions, StringComparison.Ordinal);
        Assert.Contains("possibilities", resolved.SystemInstructions, StringComparison.Ordinal);
        Assert.Contains("existing physician opinion", resolved.SystemInstructions,
            StringComparison.Ordinal);
        Assert.Contains("test, exam, or study", resolved.SystemInstructions,
            StringComparison.Ordinal);
        Assert.Contains("insufficient", resolved.SystemInstructions, StringComparison.Ordinal);
        Assert.Contains("override Pre-Triage", resolved.SystemInstructions,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredResult_AcceptsExactFiveSectionsAndDisclaimer()
    {
        Assert.True(schema.Validate(Parse(Valid())).IsValid);
    }

    [Theory]
    [InlineData("missingSummary")]
    [InlineData("extraProperty")]
    [InlineData("wrongVersion")]
    [InlineData("emptySummary")]
    [InlineData("wrongImportantPointsType")]
    [InlineData("wrongQuestionsType")]
    [InlineData("wrongMissingInformationType")]
    [InlineData("emptyArrayItem")]
    [InlineData("wrongDisclaimer")]
    public void StructuredResult_RejectsMalformedOrUnapprovedShapes(string variant)
    {
        var json = variant switch
        {
            "missingSummary" => $$"""
                {"schemaVersion":"v1","importantPoints":[],"possibleQuestionsForDoctor":[],"missingInformation":[],"disclaimer":"{{SecondOpinionProductContent.Disclaimer}}"}
                """,
            "extraProperty" => Valid()[..^1] + ",\"diagnosis\":\"x\"}",
            "wrongVersion" => Valid().Replace("\"v1\"", "\"v2\"", StringComparison.Ordinal),
            "emptySummary" => Valid().Replace("\"Educational summary\"", "\"   \"",
                StringComparison.Ordinal),
            "wrongImportantPointsType" => Valid().Replace(
                "\"importantPoints\":[\"Point\"]",
                "\"importantPoints\":\"Point\"",
                StringComparison.Ordinal),
            "wrongQuestionsType" => Valid().Replace(
                "\"possibleQuestionsForDoctor\":[\"Question?\"]",
                "\"possibleQuestionsForDoctor\":null",
                StringComparison.Ordinal),
            "wrongMissingInformationType" => Valid().Replace(
                "\"missingInformation\":[\"More context\"]",
                "\"missingInformation\":{}",
                StringComparison.Ordinal),
            "emptyArrayItem" => Valid().Replace("[\"Point\"]", "[\"\"]",
                StringComparison.Ordinal),
            "wrongDisclaimer" => Valid().Replace(
                SecondOpinionProductContent.Disclaimer,
                "Provider disclaimer",
                StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };

        Assert.False(schema.Validate(Parse(json)).IsValid);
    }

    [Theory]
    [InlineData("Possible causes could include dehydration or a tension headache.")]
    [InlineData("A neurologist may be a relevant specialty to discuss with your doctor.")]
    [InlineData("The supplied physician opinion can be discussed alongside the recorded facts.")]
    [InlineData("There is not enough information to offer more than general educational context.")]
    [InlineData("The supplied MRI report describes a finding that a doctor can explain.")]
    public void QualifiedEducationalSecondOpinionContent_IsApproved(string content)
    {
        Assert.Equal(AiSafetyCategory.Approved, Validate(content).Category);
    }

    [Theory]
    [InlineData("You have diabetes.", AiSafetyReasonCode.DefinitiveDiagnosis)]
    [InlineData("Your diagnosis is pneumonia.", AiSafetyReasonCode.DefinitiveDiagnosis)]
    [InlineData("There is an 80% chance you have migraine.",
        AiSafetyReasonCode.DiseaseProbability)]
    [InlineData("I prescribe amoxicillin.", AiSafetyReasonCode.PrescriptionInstruction)]
    [InlineData("Start taking ibuprofen.", AiSafetyReasonCode.MedicationStart)]
    [InlineData("Stop taking metformin.", AiSafetyReasonCode.MedicationStop)]
    [InlineData("Change your medication.", AiSafetyReasonCode.MedicationChange)]
    [InlineData("Increase your dosage.", AiSafetyReasonCode.DosageChange)]
    [InlineData("You should get an MRI.", AiSafetyReasonCode.NewStudyRecommendation)]
    [InlineData("I recommend a blood test.", AiSafetyReasonCode.NewStudyRecommendation)]
    [InlineData("Ask your doctor to schedule an ultrasound.",
        AiSafetyReasonCode.NewStudyRecommendation)]
    [InlineData("I recommend that you get an MRI.",
        AiSafetyReasonCode.NewStudyRecommendation)]
    [InlineData("Your urgency is high.", AiSafetyReasonCode.AuthoritativeUrgency)]
    [InlineData("Call 911 now.", AiSafetyReasonCode.EmergencyInstruction)]
    public void ProhibitedSecondOpinionContent_IsRejected(
        string content,
        AiSafetyReasonCode expected)
    {
        var decision = Validate(content);

        Assert.Equal(expected, decision.ReasonCode);
        Assert.False(decision.DisplayEligible);
    }

    [Fact]
    public void NewStudyRule_IsScopedToSecondOpinionWorkload()
    {
        var json = ResultJson("You should get an MRI.");

        var secondOpinion = safety.Validate(new AiSafetyValidationInput(
            AiWorkloadIdentifiers.SecondOpinion,
            json));
        var conversation = safety.Validate(new AiSafetyValidationInput(
            AiWorkloadIdentifiers.Conversation,
            json));

        Assert.Equal(AiSafetyReasonCode.NewStudyRecommendation, secondOpinion.ReasonCode);
        Assert.Equal(AiSafetyReasonCode.Approved, conversation.ReasonCode);
    }

    [Fact]
    public void CriticalContent_RequestsExistingFixedCriticalFallback()
    {
        var decision = Validate("Call 911 now.");

        Assert.True(decision.UseCriticalFallback);
        Assert.Equal("ai-critical-fallback-v1",
            AiSafetyProductContent.Current.CriticalFallbackVersion);
    }

    private AiSafetyDecision Validate(string summary) => safety.Validate(
        new AiSafetyValidationInput(
            AiWorkloadIdentifiers.SecondOpinion,
            ResultJson(summary)));

    private static string ResultJson(string summary) => JsonSerializer.Serialize(new
    {
        schemaVersion = "v1",
        summary,
        importantPoints = Array.Empty<string>(),
        possibleQuestionsForDoctor = Array.Empty<string>(),
        missingInformation = Array.Empty<string>(),
        disclaimer = SecondOpinionProductContent.Disclaimer
    });

    private static string Valid() => JsonSerializer.Serialize(new
    {
        schemaVersion = "v1",
        summary = "Educational summary",
        importantPoints = new[] { "Point" },
        possibleQuestionsForDoctor = new[] { "Question?" },
        missingInformation = new[] { "More context" },
        disclaimer = SecondOpinionProductContent.Disclaimer
    });

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
