using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Ai;

public static class SecondOpinionOptions
{
    public const int MaximumTypedTextCharacters = 8_000;
    public const int MaximumDocumentTextCharacters = 64_000;
    public const int MaximumClinicalHistoryEvents = 3;
}

public sealed record SecondOpinionProductContent
{
    public const string DisclaimerVersion = "ai-second-opinion-disclaimer-v1";
    public const string ResultVersion = "ai-second-opinion-result@v1";
    public const string Disclaimer =
        "This is not a medical diagnosis. Beeexy AI offers educational insights based on " +
        "clinical literature, not a substitute for a licensed physician. Always discuss " +
        "results with your doctor.";

    public static SecondOpinionProductContent Current { get; } = new();
}

public sealed record RequestSecondOpinionCommand(
    EntityId PatientProfileId,
    string? Text,
    IReadOnlyList<EntityId>? DocumentIds,
    EntityId? PreTriageSessionId,
    IReadOnlyList<EntityId>? ClinicalHistoryEventIds,
    string CorrelationIdentifier);

public sealed record SecondOpinionPreparedInput(
    string ProviderInputJson,
    string ImmutableInputJson,
    AiUploadedDocument? Document);

public interface ISecondOpinionInputAssembler
{
    Task<SecondOpinionPreparedInput> AssembleAsync(
        RequestSecondOpinionCommand command,
        EntityId accountId,
        CancellationToken cancellationToken = default);
}

public enum SecondOpinionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Rejected
}

public sealed record SecondOpinionRequestReceipt(
    EntityId AnalysisId,
    EntityId ExecutionId,
    SecondOpinionStatus Status);

public sealed record SecondOpinionResult(
    string Summary,
    IReadOnlyList<string> ImportantPoints,
    IReadOnlyList<string> PossibleQuestionsForDoctor,
    IReadOnlyList<string> MissingInformation,
    string Disclaimer);

public sealed record SecondOpinionMetadata(
    bool AiGenerated,
    DateTimeOffset GeneratedAt,
    string ResultVersion,
    string? Provider,
    string? ModelVersion,
    string? PromptVersion,
    string DisclaimerVersion);

public sealed record SecondOpinionDetail(
    EntityId AnalysisId,
    EntityId PatientProfileId,
    EntityId? ExecutionId,
    SecondOpinionStatus Status,
    SecondOpinionResult? Result,
    SecondOpinionMetadata? Metadata,
    string? SafeMessage);

public sealed record SecondOpinionStoredState(
    AiExecutionStatus? ExecutionStatus,
    EntityId? ExecutionId,
    string? ProviderIdentifier,
    string? ModelIdentifier,
    string? PromptVersion,
    string? ResultContentJson,
    DateTimeOffset? ResultCreatedAt,
    AiSafetyCategory? SafetyCategory,
    bool? DisplayEligible,
    string? ProductContentVersion);

public sealed record SecondOpinionAnalysisAccess(
    EntityId AnalysisId,
    EntityId PatientProfileId);

public interface ISecondOpinionRepository
{
    void Add(AiAnalysisRequest request);

    Task<SecondOpinionAnalysisAccess?> FindOwnedAsync(
        EntityId analysisId,
        EntityId accountId,
        CancellationToken cancellationToken = default);

    Task<SecondOpinionStoredState> GetStateAsync(
        EntityId analysisId,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class SecondOpinionNotFoundException : Exception
{
    public SecondOpinionNotFoundException()
        : base("The requested Second Opinion could not be found.")
    {
    }
}
