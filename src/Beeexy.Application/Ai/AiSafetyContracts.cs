using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Ai;

public enum AiSafetyReasonCode
{
    Approved,
    UnsafeCareInstruction,
    DefinitiveDiagnosis,
    PrescriptionInstruction,
    MedicationStart,
    MedicationStop,
    MedicationChange,
    DosageChange,
    AuthoritativeUrgency,
    EmergencyInstruction,
    DiseaseProbability,
    UnsupportedOutput,
    MalformedOutput
}

public sealed record AiSafetyValidationInput(
    string WorkloadIdentifier,
    string StructurallyValidatedContent);

public sealed record AiSafetyDecision
{
    private AiSafetyDecision(
        AiSafetyCategory category,
        AiSafetyReasonCode reasonCode,
        string policyVersion,
        bool useCriticalFallback)
    {
        Category = category;
        ReasonCode = reasonCode;
        PolicyVersion = AiContractGuard.Identifier(policyVersion, nameof(policyVersion));
        UseCriticalFallback = useCriticalFallback;
    }

    public AiSafetyCategory Category { get; }

    public AiSafetyReasonCode ReasonCode { get; }

    public string PolicyVersion { get; }

    public bool IsApproved => Category == AiSafetyCategory.Approved;

    public bool DisplayEligible => IsApproved;

    public bool UseCriticalFallback { get; }

    public static AiSafetyDecision Approved(string policyVersion) => new(
        AiSafetyCategory.Approved,
        AiSafetyReasonCode.Approved,
        policyVersion,
        useCriticalFallback: false);

    public static AiSafetyDecision Rejected(
        AiSafetyCategory category,
        AiSafetyReasonCode reasonCode,
        string policyVersion,
        bool useCriticalFallback = false)
    {
        if (!Enum.IsDefined(category) || category == AiSafetyCategory.Approved)
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        if (!Enum.IsDefined(reasonCode) || reasonCode == AiSafetyReasonCode.Approved)
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode));
        }

        return new AiSafetyDecision(
            category,
            reasonCode,
            policyVersion,
            useCriticalFallback);
    }
}

public interface IAiSafetyValidator
{
    AiSafetyDecision Validate(AiSafetyValidationInput input);
}

public sealed record AiSafetyProductContent
{
    public AiSafetyProductContent(
        string policyVersion,
        string disclaimerVersion,
        string disclaimer,
        string genericFallbackVersion,
        string genericFallback,
        string criticalFallbackVersion,
        string criticalFallback)
    {
        PolicyVersion = AiContractGuard.Identifier(policyVersion, nameof(policyVersion));
        DisclaimerVersion = AiContractGuard.Identifier(
            disclaimerVersion,
            nameof(disclaimerVersion));
        Disclaimer = AiContractGuard.Content(disclaimer, nameof(disclaimer));
        GenericFallbackVersion = AiContractGuard.Identifier(
            genericFallbackVersion,
            nameof(genericFallbackVersion));
        GenericFallback = AiContractGuard.Content(
            genericFallback,
            nameof(genericFallback));
        CriticalFallbackVersion = AiContractGuard.Identifier(
            criticalFallbackVersion,
            nameof(criticalFallbackVersion));
        CriticalFallback = AiContractGuard.Content(
            criticalFallback,
            nameof(criticalFallback));
    }

    public string PolicyVersion { get; }

    public string DisclaimerVersion { get; }

    public string Disclaimer { get; }

    public string GenericFallbackVersion { get; }

    public string GenericFallback { get; }

    public string CriticalFallbackVersion { get; }

    public string CriticalFallback { get; }

    public static AiSafetyProductContent Current { get; } = new(
        "ai-safety-policy-v1",
        "ai-general-disclaimer-v1",
        "Esta respuesta ha sido generada por inteligencia artificial y no sustituye " +
        "una evaluación médica. Consulta siempre con un profesional de salud certificado.",
        "ai-rejection-fallback-v1",
        "Esta respuesta no puede mostrarse porque no cumple con las reglas de seguridad " +
        "de Beeexy. Consulta con un profesional de salud certificado.",
        "ai-critical-fallback-v1",
        "La información proporcionada podría requerir atención médica. Si crees que puedes " +
        "estar ante una emergencia o tus síntomas son graves, busca atención médica de inmediato.");
}

public sealed record ExecuteSafeAiAnalysisCommand(ExecuteAiAnalysisCommand Execution);

public sealed record AiSafeAnalysisOutcome(
    EntityId ExecutionId,
    AiExecutionOutcomeKind TechnicalOutcome,
    AiSafetyCategory? SafetyCategory,
    bool ProviderOutputDisplayEligible,
    string? ResponseContent,
    string? Disclaimer,
    string? ProductContentVersion,
    EntityId? SafetyValidationId,
    EntityId? ResultSnapshotId);

public interface IAiSafetyPersistence
{
    void AddApproved(AiResultSnapshot snapshot, AiSafetyValidation validation);

    void AddRejected(AiSafetyValidation validation);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IAiSafetyTelemetry
{
    void DecisionPersisted(AiSafetyValidation validation);
}
