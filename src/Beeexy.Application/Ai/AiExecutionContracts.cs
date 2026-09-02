using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Ai;

public static class AiWorkloadIdentifiers
{
    public const string Conversation = "ai-conversation";
    public const string SecondOpinion = "ai-second-opinion";
}

public static class AiPromptContractIdentifiers
{
    public const string Conversation = "ai-conversation";
    public const string SecondOpinion = "ai-second-opinion";
    public const string SafetyFallback = "ai-safety-fallback";
}

public sealed record AiPromptIdentity
{
    public AiPromptIdentity(string contractIdentifier, string version)
    {
        ContractIdentifier = AiContractGuard.Identifier(
            contractIdentifier,
            nameof(contractIdentifier));
        Version = AiContractGuard.Identifier(version, nameof(version));
        PersistenceValue = AiContractGuard.Identifier(
            $"{ContractIdentifier}@{Version}",
            nameof(PersistenceValue));
    }

    public string ContractIdentifier { get; }

    public string Version { get; }

    public string PersistenceValue { get; }
}

public sealed record AiStructuredResultIdentity
{
    public AiStructuredResultIdentity(string schemaIdentifier, string version)
    {
        SchemaIdentifier = AiContractGuard.Identifier(
            schemaIdentifier,
            nameof(schemaIdentifier));
        Version = AiContractGuard.Identifier(version, nameof(version));
    }

    public string SchemaIdentifier { get; }

    public string Version { get; }
}

public sealed record ExecuteAiAnalysisCommand
{
    public ExecuteAiAnalysisCommand(
        EntityId analysisRequestId,
        string workloadIdentifier,
        AiPromptIdentity prompt,
        string preparedInput,
        AiStructuredResultIdentity expectedResult,
        string correlationIdentifier)
    {
        if (analysisRequestId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "The analysis request identifier cannot be empty.",
                nameof(analysisRequestId));
        }

        AnalysisRequestId = analysisRequestId;
        WorkloadIdentifier = AiContractGuard.Identifier(
            workloadIdentifier,
            nameof(workloadIdentifier));
        Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        PreparedInput = AiContractGuard.Content(preparedInput, nameof(preparedInput));
        ExpectedResult = expectedResult ??
            throw new ArgumentNullException(nameof(expectedResult));
        CorrelationIdentifier = AiContractGuard.Identifier(
            correlationIdentifier,
            nameof(correlationIdentifier));
    }

    public EntityId AnalysisRequestId { get; }

    public string WorkloadIdentifier { get; }

    public AiPromptIdentity Prompt { get; }

    public string PreparedInput { get; }

    public AiStructuredResultIdentity ExpectedResult { get; }

    public string CorrelationIdentifier { get; }
}

public sealed record AiProviderRequest(
    string WorkloadIdentifier,
    AiPromptIdentity Prompt,
    string SystemInstructions,
    string UserContent,
    AiStructuredResultIdentity ExpectedResult,
    string CorrelationIdentifier);

public sealed record AiProviderResponse(string StructuredContent);

public enum AiProviderFailureCategory
{
    Timeout,
    Transient,
    Permanent,
    MalformedResponse,
    ConfigurationUnavailable
}

public sealed class AiProviderException : Exception
{
    public AiProviderException(AiProviderFailureCategory category)
        : base(SafeMessage(category))
    {
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        Category = category;
    }

    public AiProviderFailureCategory Category { get; }

    private static string SafeMessage(AiProviderFailureCategory category) => category switch
    {
        AiProviderFailureCategory.Timeout => "AI provider execution timed out.",
        AiProviderFailureCategory.Transient => "AI provider execution failed temporarily.",
        AiProviderFailureCategory.Permanent => "AI provider execution failed.",
        AiProviderFailureCategory.MalformedResponse =>
            "AI provider execution returned an invalid response.",
        AiProviderFailureCategory.ConfigurationUnavailable =>
            "AI provider execution is not configured.",
        _ => "AI provider execution failed safely."
    };
}

public interface IAiProvider
{
    string ProviderIdentifier { get; }

    string ModelIdentifier { get; }

    Task<AiProviderResponse> ExecuteAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default);
}

public enum AiExecutionOutcomeKind
{
    StructurallyValid,
    MalformedResult,
    Timeout,
    CallerCancelled,
    TransientFailure,
    PermanentFailure,
    ConfigurationUnavailable
}

public sealed record AiExecutionOutcome(
    EntityId ExecutionId,
    AiExecutionOutcomeKind Kind,
    string? StructurallyValidatedContent = null,
    AiStructuralValidationIssue? StructuralIssue = null)
{
    public bool RequiresSafetyValidation => Kind == AiExecutionOutcomeKind.StructurallyValid;
}

public interface IAiExecutionRepository
{
    void Add(AiExecution execution);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IAiExecutionTelemetry
{
    void Started(AiExecution execution);

    void Completed(AiExecution execution);
}

internal static class AiExecutionFailureCategories
{
    public const string Timeout = "timeout";
    public const string CallerCancellation = "caller_cancellation";
    public const string TransientProviderFailure = "provider_transient";
    public const string PermanentProviderFailure = "provider_permanent";
    public const string ConfigurationUnavailable = "configuration_unavailable";
}

internal static class AiContractGuard
{
    public static string Identifier(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > AiPersistenceLimits.Identifier)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The identifier cannot exceed {AiPersistenceLimits.Identifier} characters.");
        }

        return normalized;
    }

    public static string Content(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Non-empty content is required.", parameterName);
        }

        return value;
    }
}
