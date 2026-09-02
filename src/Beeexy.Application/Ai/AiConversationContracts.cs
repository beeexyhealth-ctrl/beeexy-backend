using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Ai;

public sealed record AiConversationOptions
{
    public const int MaximumMessages = 50;
    public const int MaximumMessageCharacters = 4_000;
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;
    public const int DefaultProviderContextCharacterBudget = 16_000;

    public AiConversationOptions(int providerContextCharacterBudget)
    {
        if (providerContextCharacterBudget is < 8_000 or > 64_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerContextCharacterBudget),
                "The conversation context character budget must be between 8000 and 64000.");
        }

        ProviderContextCharacterBudget = providerContextCharacterBudget;
    }

    public int ProviderContextCharacterBudget { get; }
}

public static class AiConversationPurpose
{
    public const string GeneralHealth = "GENERAL_HEALTH";
    public const string MedicalTerms = "MEDICAL_TERMS";
    public const string SymptomDiscussion = "SYMPTOM_DISCUSSION";
    public const string ClinicianQuestions = "CLINICIAN_QUESTIONS";
}

public sealed record CreateAiConversationCommand(string? Purpose, EntityId? PatientProfileId);

public sealed record AiConversationSummary(
    EntityId ConversationId,
    EntityId? PatientProfileId,
    DateTimeOffset CreatedAt);

public sealed record AiConversationMessageView(
    EntityId MessageId,
    AiMessageRole Role,
    string Content,
    int Sequence,
    DateTimeOffset CreatedAt);

public sealed record AiConversationDetail(
    AiConversationSummary Conversation,
    IReadOnlyList<AiConversationMessageView> Messages);

public sealed record AiConversationPage(
    IReadOnlyList<AiConversationSummary> Items,
    string? NextCursor);

public sealed record ListAiConversationsQuery(string? Cursor = null, int? PageSize = null);

public sealed record SendAiConversationMessageCommand(
    EntityId ConversationId,
    string? Content,
    string CorrelationIdentifier);

public enum AiConversationExecutionState
{
    Completed,
    Failed,
    Rejected
}

public sealed record SendAiConversationMessageResult(
    EntityId ConversationId,
    EntityId UserMessageId,
    EntityId ExecutionId,
    AiConversationExecutionState State,
    AiConversationMessageView? AssistantMessage);

public sealed record AiConversationPageCursor(
    EntityId AccountId,
    DateTimeOffset CreatedAt,
    EntityId ConversationId);

public sealed record AiPatientContextSource(
    string SourceType,
    EntityId SourceId,
    DateTimeOffset? OccurredAt);

public sealed record AiPatientContext(
    string ProviderNeutralJson,
    IReadOnlyList<AiPatientContextSource> Sources);

public interface IAiPatientContextAssembler
{
    Task<AiPatientContext> AssembleAsync(
        EntityId patientProfileId,
        CancellationToken cancellationToken = default);
}

public interface IAiConversationExecutionLease : IAsyncDisposable;

public interface IAiConversationRepository
{
    void Add(AiConversation conversation);

    void Add(AiMessage message);

    void Add(AiAnalysisRequest request);

    Task<AiConversation?> FindOwnedAsync(
        EntityId conversationId,
        EntityId accountId,
        bool includeDeleted,
        CancellationToken cancellationToken = default);

    Task<bool> CursorExistsAsync(
        AiConversationPageCursor cursor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiConversationSummary>> ListAsync(
        EntityId accountId,
        AiConversationPageCursor? after,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiConversationMessageView>> ListMessagesAsync(
        EntityId conversationId,
        CancellationToken cancellationToken = default);

    Task<IAiConversationExecutionLease?> TryAcquireExecutionLeaseAsync(
        EntityId conversationId,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class AiConversationNotFoundException : Exception
{
    public AiConversationNotFoundException()
        : base("The requested AI conversation could not be found.")
    {
    }
}

public sealed class AiConversationExecutionConflictException : Exception
{
    public AiConversationExecutionConflictException()
        : base("Another execution is already running for this AI conversation.")
    {
    }
}
