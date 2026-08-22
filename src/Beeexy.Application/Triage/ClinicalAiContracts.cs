using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public enum ClinicalIntentClassification
{
    PreTriageInput,
    OutOfScope,
    PrescriptionRequest,
    ProhibitedMedicalAdvice,
    PotentialPromptInjection,
    UnsupportedClinicalRequest,
    Ambiguous
}

public enum ClinicalAiConfidenceSignal
{
    Sufficient,
    Uncertain,
    Low,
    Unspecified
}

public enum ClinicalAiCandidateStatus
{
    AcceptedCandidate,
    NeedsClarification,
    Rejected,
    Unsupported
}

public enum ClinicalAiOutputViolation
{
    MalformedStructure,
    UnknownMember,
    InvalidEnum,
    ForbiddenClinicalAuthority
}

public enum ClinicalAiAmbiguityKind
{
    Pathway,
    FactValue,
    ConflictingFacts,
    InsufficientContext
}

public enum ClinicalDurationUnit
{
    Minutes,
    Hours,
    Days,
    Weeks,
    Months
}

public enum ClinicalTemperatureUnit
{
    Celsius,
    Fahrenheit
}

public abstract record ClinicalAiCandidateValue;

public sealed record ClinicalAiTextValue(string Value) : ClinicalAiCandidateValue;

public sealed record ClinicalAiChoiceValue(string Value) : ClinicalAiCandidateValue;

public sealed record ClinicalAiMultipleChoiceValue(IReadOnlyList<string> Values)
    : ClinicalAiCandidateValue;

public sealed record ClinicalAiIntegerValue(int Value) : ClinicalAiCandidateValue;

public sealed record ClinicalAiBooleanValue(bool Value) : ClinicalAiCandidateValue;

public sealed record ClinicalAiDurationValue(decimal Value, ClinicalDurationUnit Unit)
    : ClinicalAiCandidateValue;

public sealed record ClinicalAiTemperatureValue(decimal Value, ClinicalTemperatureUnit Unit)
    : ClinicalAiCandidateValue;

public sealed record ClinicalAiKnownFact(
    QuestionCode Code,
    ClinicalAiCandidateValue Value);

public sealed record ClinicalAiFactCandidate(
    QuestionCode Code,
    ClinicalAiCandidateValue Value,
    ClinicalAiConfidenceSignal Confidence);

public sealed record ClinicalAiSymptomCandidate(
    string Text,
    string? NormalizedPathwayCandidate,
    ClinicalAiConfidenceSignal Confidence);

public sealed record ClinicalAiAmbiguity(
    ClinicalAiAmbiguityKind Kind,
    QuestionCode? FactCode = null);

public sealed class ClinicalAiInterpretationRequest
{
    public ClinicalAiInterpretationRequest(
        string userMessage,
        ClinicalPathwayCode? selectedPathway = null,
        IReadOnlyList<ClinicalAiKnownFact>? knownFacts = null,
        IReadOnlyList<QuestionCode>? allowedFactCodes = null,
        ClinicalDefinitionPackage? pinnedDefinition = null)
    {
        UserMessage = userMessage ?? throw new ArgumentNullException(nameof(userMessage));
        SelectedPathway = selectedPathway;
        KnownFacts = knownFacts?.ToArray() ?? [];
        AllowedFactCodes = allowedFactCodes?.ToArray() ?? [];
        PinnedDefinition = pinnedDefinition;
    }

    public string UserMessage { get; }

    public ClinicalPathwayCode? SelectedPathway { get; }

    public IReadOnlyList<ClinicalAiKnownFact> KnownFacts { get; }

    public IReadOnlyList<QuestionCode> AllowedFactCodes { get; }

    public ClinicalDefinitionPackage? PinnedDefinition { get; }
}

public sealed record ClinicalAiProviderOutput(
    string? SchemaVersion,
    ClinicalIntentClassification Intent,
    string? PathwayCandidate,
    IReadOnlyList<ClinicalAiFactCandidate>? Facts,
    IReadOnlyList<ClinicalAiSymptomCandidate>? Symptoms,
    IReadOnlyList<ClinicalAiAmbiguity>? Ambiguities,
    bool RequiresClarification,
    IReadOnlyList<ClinicalAiOutputViolation>? SchemaViolations = null)
{
    public const string CurrentSchemaVersion = "clinical-interpretation-v1";
}

public interface IClinicalAiProvider
{
    Task<ClinicalAiProviderOutput> InterpretAsync(
        ClinicalAiInterpretationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ClinicalSafetyDecision(
    ClinicalIntentClassification Classification,
    bool AllowsProviderInterpretation,
    bool RequiresClarification);

public interface IClinicalSafetyPolicy
{
    ClinicalSafetyDecision EvaluateInput(ClinicalAiInterpretationRequest request);

    ClinicalSafetyDecision EvaluateOutput(
        ClinicalAiInterpretationRequest request,
        ClinicalAiProviderOutput output);
}

public enum ClinicalAiValidationOutcome
{
    Accepted,
    NeedsClarification,
    Rejected,
    Unsupported
}

public enum ClinicalAiValidationIssue
{
    MalformedProviderOutput,
    InvalidIntent,
    ForbiddenClinicalAuthority,
    MissingPathway,
    UnknownPathway,
    RecognizedButUnsupportedPathway,
    MissingActiveDefinition,
    PathwayMismatch,
    UnknownFactCode,
    FactOutsideAllowedVocabulary,
    WrongAnswerType,
    InvalidChoice,
    ValueOutsideRange,
    InvalidDuration,
    InvalidTemperature,
    InsufficientConfidence,
    AmbiguousOutput,
    ConflictingFact,
    InvalidSymptomCandidate
}

public sealed record ClinicalAiValidatedFactCandidate(
    QuestionCode Code,
    ClinicalAiCandidateValue Value,
    ClinicalAiCandidateStatus Status,
    ClinicalAiValidationIssue? Issue = null,
    bool MatchesKnownFact = false);

public sealed record ClinicalAiValidatedSymptomCandidate(
    string Text,
    ClinicalPathwayCode? Pathway,
    ClinicalAiCandidateStatus Status,
    ClinicalAiValidationIssue? Issue = null);

public sealed record ClinicalAiOutputValidationResult(
    ClinicalAiValidationOutcome Outcome,
    ClinicalPathwayResolutionStatus? PathwayStatus,
    ClinicalPathwayCode? Pathway,
    IReadOnlyList<ClinicalAiValidatedFactCandidate> Facts,
    IReadOnlyList<ClinicalAiValidatedSymptomCandidate> Symptoms,
    IReadOnlyList<ClinicalAiValidationIssue> Issues);

public interface IClinicalAiOutputValidator
{
    Task<ClinicalAiOutputValidationResult> ValidateAsync(
        ClinicalAiInterpretationRequest request,
        ClinicalAiProviderOutput output,
        CancellationToken cancellationToken = default);
}

public enum ClinicalAiProviderFailureCategory
{
    Unavailable,
    Timeout,
    InvalidStructuredResponse,
    RejectedOutput,
    ConfigurationUnavailable
}

public sealed class ClinicalAiProviderException : Exception
{
    public ClinicalAiProviderException(ClinicalAiProviderFailureCategory category)
        : base(SafeMessage(category))
    {
        Category = category;
    }

    public ClinicalAiProviderFailureCategory Category { get; }

    private static string SafeMessage(ClinicalAiProviderFailureCategory category)
    {
        return category switch
        {
            ClinicalAiProviderFailureCategory.Unavailable =>
                "Clinical interpretation is temporarily unavailable.",
            ClinicalAiProviderFailureCategory.Timeout =>
                "Clinical interpretation did not complete in time.",
            ClinicalAiProviderFailureCategory.InvalidStructuredResponse =>
                "Clinical interpretation returned an invalid structured response.",
            ClinicalAiProviderFailureCategory.RejectedOutput =>
                "Clinical interpretation output was rejected.",
            ClinicalAiProviderFailureCategory.ConfigurationUnavailable =>
                "Clinical interpretation is not configured.",
            _ => "Clinical interpretation failed safely."
        };
    }
}

public enum ClinicalAiInterpretationOutcome
{
    Accepted,
    ClarificationRequired,
    SafetyRestricted,
    Unsupported,
    ProviderUnavailable,
    ProviderTimeout,
    InvalidProviderOutput,
    ProviderRejected,
    ConfigurationUnavailable
}

public sealed record ClinicalAiInterpretationResult(
    ClinicalAiInterpretationOutcome Outcome,
    ClinicalIntentClassification SafetyClassification,
    ClinicalAiOutputValidationResult? Validation,
    ClinicalAiProviderFailureCategory? ProviderFailure = null);
