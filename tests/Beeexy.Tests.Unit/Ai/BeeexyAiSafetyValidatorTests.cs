using System.Text.Json;
using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;

namespace Beeexy.Tests.Unit.Ai;

[Trait("Category", "Phase103")]
public sealed class BeeexyAiSafetyValidatorTests
{
    private readonly BeeexyAiSafetyValidator validator = new(AiSafetyProductContent.Current);

    [Fact]
    public void InformationalOutput_IsApprovedAndDisplayEligible()
    {
        var decision = Validate(
            "A headache can have several causes. A clinician can help review the context.");

        Assert.Equal(AiSafetyCategory.Approved, decision.Category);
        Assert.Equal(AiSafetyReasonCode.Approved, decision.ReasonCode);
        Assert.True(decision.IsApproved);
        Assert.True(decision.DisplayEligible);
        Assert.False(decision.UseCriticalFallback);
        Assert.Equal("ai-safety-policy-v1", decision.PolicyVersion);
    }

    [Theory]
    [InlineData("You have diabetes.")]
    [InlineData("Your diagnosis is pneumonia.")]
    [InlineData("This confirms influenza.")]
    [InlineData("I diagnose you with migraine.")]
    public void DefinitiveDiagnosis_IsRejected(string text)
    {
        var decision = Validate(text);

        Assert.Equal(AiSafetyCategory.Diagnosis, decision.Category);
        Assert.Equal(AiSafetyReasonCode.DefinitiveDiagnosis, decision.ReasonCode);
        Assert.False(decision.DisplayEligible);
    }

    [Theory]
    [InlineData("I prescribe amoxicillin.", AiSafetyReasonCode.PrescriptionInstruction)]
    [InlineData("Start taking the medication.", AiSafetyReasonCode.MedicationStart)]
    [InlineData("Start taking ibuprofen.", AiSafetyReasonCode.MedicationStart)]
    [InlineData("Stop taking your medicine.", AiSafetyReasonCode.MedicationStop)]
    [InlineData("Stop taking metformin.", AiSafetyReasonCode.MedicationStop)]
    [InlineData("Change your medication.", AiSafetyReasonCode.MedicationChange)]
    [InlineData("Increase your dosage.", AiSafetyReasonCode.DosageChange)]
    [InlineData("Take 500 mg.", AiSafetyReasonCode.PrescriptionInstruction)]
    public void PrescriptionAndMedicationInstructions_AreRejected(
        string text,
        AiSafetyReasonCode expectedReason)
    {
        var decision = Validate(text);

        Assert.Equal(AiSafetyCategory.Prescription, decision.Category);
        Assert.Equal(expectedReason, decision.ReasonCode);
        Assert.False(decision.DisplayEligible);
    }

    [Theory]
    [InlineData(
        "Treat this at home and ignore the symptoms.",
        AiSafetyReasonCode.UnsafeCareInstruction,
        false)]
    [InlineData(
        "Your urgency is high.",
        AiSafetyReasonCode.AuthoritativeUrgency,
        true)]
    [InlineData(
        "I classify this as urgent.",
        AiSafetyReasonCode.AuthoritativeUrgency,
        true)]
    [InlineData(
        "This is an emergency.",
        AiSafetyReasonCode.AuthoritativeUrgency,
        true)]
    [InlineData(
        "Call 911 now.",
        AiSafetyReasonCode.EmergencyInstruction,
        true)]
    [InlineData(
        "Go to the emergency room immediately.",
        AiSafetyReasonCode.EmergencyInstruction,
        true)]
    public void UnsafeAdviceUrgencyAndEmergencyInstructions_AreRejected(
        string text,
        AiSafetyReasonCode expectedReason,
        bool criticalFallback)
    {
        var decision = Validate(text);

        Assert.Equal(AiSafetyCategory.UnsafeMedicalAdvice, decision.Category);
        Assert.Equal(expectedReason, decision.ReasonCode);
        Assert.Equal(criticalFallback, decision.UseCriticalFallback);
        Assert.False(decision.DisplayEligible);
    }

    [Theory]
    [InlineData("There is an 80% chance you have diabetes.")]
    [InlineData("You are 70% likely to have pneumonia.")]
    [InlineData("The probability that you have influenza is 65%.")]
    public void NumericalDiseaseProbability_IsRejected(string text)
    {
        var decision = Validate(text);

        Assert.Equal(AiSafetyCategory.Diagnosis, decision.Category);
        Assert.Equal(AiSafetyReasonCode.DiseaseProbability, decision.ReasonCode);
        Assert.False(decision.DisplayEligible);
    }

    [Theory]
    [InlineData(
        "{\"schemaVersion\":\"v1\",\"diagnosis\":\"diabetes\"}",
        AiSafetyCategory.Diagnosis,
        AiSafetyReasonCode.DefinitiveDiagnosis)]
    [InlineData(
        "{\"schemaVersion\":\"v1\",\"diseaseProbability\":0.8}",
        AiSafetyCategory.Diagnosis,
        AiSafetyReasonCode.DiseaseProbability)]
    [InlineData(
        "{\"schemaVersion\":\"v1\",\"urgencyClassification\":\"high\"}",
        AiSafetyCategory.UnsafeMedicalAdvice,
        AiSafetyReasonCode.AuthoritativeUrgency)]
    public void ProhibitedStructuredAuthorityFields_AreRejected(
        string json,
        AiSafetyCategory expectedCategory,
        AiSafetyReasonCode expectedReason)
    {
        var decision = validator.Validate(Input(json));

        Assert.Equal(expectedCategory, decision.Category);
        Assert.Equal(expectedReason, decision.ReasonCode);
        Assert.False(decision.DisplayEligible);
    }

    [Theory]
    [InlineData("Possible considerations include migraine and tension headache.")]
    [InlineData("One possibility that could be discussed with a physician is migraine.")]
    [InlineData("This information may be associated with several possible causes.")]
    public void NonDiagnosticPossibilityLanguage_MayBeApproved(string text)
    {
        var decision = Validate(text);

        Assert.Equal(AiSafetyCategory.Approved, decision.Category);
        Assert.True(decision.DisplayEligible);
    }

    [Theory]
    [InlineData("Diabetes is a condition involving blood glucose.")]
    [InlineData("Pneumonia can be discussed with a healthcare professional.")]
    [InlineData("Migraine is one possible consideration.")]
    public void NeutralDiseaseMention_IsNotAutomaticallyDiagnosis(string text)
    {
        Assert.Equal(AiSafetyCategory.Approved, Validate(text).Category);
    }

    [Theory]
    [InlineData("{\"status\":\"unsupported\",\"answer\":\"not available\"}")]
    [InlineData("{\"supported\":false,\"answer\":\"not available\"}")]
    public void ExplicitUnsupportedOutput_IsRejected(string json)
    {
        var decision = validator.Validate(Input(json));

        Assert.Equal(AiSafetyCategory.Unsupported, decision.Category);
        Assert.Equal(AiSafetyReasonCode.UnsupportedOutput, decision.ReasonCode);
        Assert.False(decision.DisplayEligible);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("")]
    public void MalformedDirectInput_UsesConsistentCategory(string content)
    {
        var decision = validator.Validate(Input(content));

        Assert.Equal(AiSafetyCategory.Malformed, decision.Category);
        Assert.Equal(AiSafetyReasonCode.MalformedOutput, decision.ReasonCode);
        Assert.False(decision.DisplayEligible);
    }

    [Fact]
    public void ProductContent_IsVersionedAndBeeexyControlled()
    {
        var content = AiSafetyProductContent.Current;

        Assert.Equal("ai-safety-policy-v1", content.PolicyVersion);
        Assert.Equal("ai-general-disclaimer-v1", content.DisclaimerVersion);
        Assert.Contains("no sustituye una evaluación médica", content.Disclaimer,
            StringComparison.Ordinal);
        Assert.Equal("ai-rejection-fallback-v1", content.GenericFallbackVersion);
        Assert.DoesNotContain("provider", content.GenericFallback,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ai-critical-fallback-v1", content.CriticalFallbackVersion);
        Assert.Contains("busca atención médica de inmediato", content.CriticalFallback,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatorHasNoProviderClinicalHistoryOrFhirDependency()
    {
        var dependencies = typeof(BeeexyAiSafetyValidator).GetConstructors().Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.FullName!)
            .ToArray();

        Assert.DoesNotContain(dependencies, name =>
            name.Contains("Provider", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("History", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Fhir", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Triage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RejectedDecisionCannotClaimApprovalOrDisplayEligibility()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AiSafetyDecision.Rejected(
            AiSafetyCategory.Approved,
            AiSafetyReasonCode.DefinitiveDiagnosis,
            "policy-v1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => AiSafetyDecision.Rejected(
            AiSafetyCategory.Diagnosis,
            AiSafetyReasonCode.Approved,
            "policy-v1"));
    }

    private AiSafetyDecision Validate(string text) => validator.Validate(Input(Json(text)));

    private static AiSafetyValidationInput Input(string content) => new(
        AiWorkloadIdentifiers.Conversation,
        content);

    private static string Json(string text) => JsonSerializer.Serialize(new
    {
        schemaVersion = "v1",
        answer = text
    });
}
