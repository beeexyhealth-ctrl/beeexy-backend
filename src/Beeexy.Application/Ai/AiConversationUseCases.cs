using System.Text;
using System.Text.Json;
using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Ai;

public sealed class CreateAiConversation(
    ICurrentSessionIdentity currentIdentity,
    AuthorizePatientAccess authorizePatientAccess,
    IAiConversationRepository repository,
    AiConversationRequestPolicy requestPolicy,
    IClock clock)
{
    public async Task<AiConversationSummary> ExecuteAsync(
        CreateAiConversationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        requestPolicy.ValidatePurpose(command.Purpose);
        if (command.PatientProfileId is { } patientProfileId)
        {
            var authorization = await authorizePatientAccess.ExecuteAsync(
                patientProfileId,
                cancellationToken);
            if (!authorization.IsAuthorized)
            {
                throw new PatientProfileNotFoundException();
            }
        }

        var conversation = AiConversation.Create(
            currentIdentity.GetRequired().AccountId,
            clock.UtcNow,
            command.PatientProfileId);
        repository.Add(conversation);
        await repository.SaveChangesAsync(cancellationToken);
        return ToSummary(conversation);
    }

    internal static AiConversationSummary ToSummary(AiConversation conversation) => new(
        conversation.Id,
        conversation.PatientProfileId,
        conversation.CreatedAt);
}

public sealed class ListAiConversations(
    ICurrentSessionIdentity currentIdentity,
    IAiConversationRepository repository)
{
    public async Task<AiConversationPage> ExecuteAsync(
        ListAiConversationsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var current = currentIdentity.GetRequired();
        var pageSize = query.PageSize ?? AiConversationOptions.DefaultPageSize;
        if (pageSize is < 1 or > AiConversationOptions.MaximumPageSize)
        {
            throw new RequestValidationException(
                "ai.conversation.page_size_invalid",
                $"Page size must be between 1 and {AiConversationOptions.MaximumPageSize}.");
        }

        var cursor = query.Cursor is null
            ? null
            : AiConversationCursorCodec.Decode(query.Cursor, current.AccountId);
        if (cursor is not null &&
            !await repository.CursorExistsAsync(cursor, cancellationToken))
        {
            throw AiConversationCursorCodec.Invalid();
        }

        var page = await repository.ListAsync(
            current.AccountId,
            cursor,
            pageSize + 1,
            cancellationToken);
        var hasMore = page.Count > pageSize;
        var items = page.Take(pageSize).ToArray();
        var nextCursor = hasMore
            ? AiConversationCursorCodec.Encode(new AiConversationPageCursor(
                current.AccountId,
                items[^1].CreatedAt,
                items[^1].ConversationId))
            : null;
        return new AiConversationPage(items, nextCursor);
    }
}

public sealed class GetAiConversation(
    ICurrentSessionIdentity currentIdentity,
    IAiConversationRepository repository)
{
    public async Task<AiConversationDetail> ExecuteAsync(
        EntityId conversationId,
        CancellationToken cancellationToken = default)
    {
        var current = currentIdentity.GetRequired();
        var conversation = await repository.FindOwnedAsync(
            conversationId,
            current.AccountId,
            includeDeleted: false,
            cancellationToken) ?? throw new AiConversationNotFoundException();
        var messages = await repository.ListMessagesAsync(
            conversation.Id,
            cancellationToken);
        return new AiConversationDetail(
            CreateAiConversation.ToSummary(conversation),
            messages);
    }
}

public sealed class DeleteAiConversation(
    ICurrentSessionIdentity currentIdentity,
    IAiConversationRepository repository,
    IClock clock)
{
    public async Task ExecuteAsync(
        EntityId conversationId,
        CancellationToken cancellationToken = default)
    {
        var current = currentIdentity.GetRequired();
        var conversation = await repository.FindOwnedAsync(
            conversationId,
            current.AccountId,
            includeDeleted: true,
            cancellationToken) ?? throw new AiConversationNotFoundException();
        if (conversation.Delete(clock.UtcNow))
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed class SendAiConversationMessage(
    ICurrentSessionIdentity currentIdentity,
    IAiConversationRepository repository,
    IAiPatientContextAssembler patientContextAssembler,
    AiConversationRequestPolicy requestPolicy,
    AiConversationContextBuilder contextBuilder,
    ExecuteSafeAiAnalysis safeExecution,
    IClock clock)
{
    public async Task<SendAiConversationMessageResult> ExecuteAsync(
        SendAiConversationMessageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var messageContent = requestPolicy.ValidateMessage(command.Content);
        var current = currentIdentity.GetRequired();
        var conversation = await repository.FindOwnedAsync(
            command.ConversationId,
            current.AccountId,
            includeDeleted: false,
            cancellationToken) ?? throw new AiConversationNotFoundException();

        await using var executionLease = await repository.TryAcquireExecutionLeaseAsync(
            conversation.Id,
            cancellationToken) ?? throw new AiConversationExecutionConflictException();

        conversation = await repository.FindOwnedAsync(
            command.ConversationId,
            current.AccountId,
            includeDeleted: false,
            cancellationToken) ?? throw new AiConversationNotFoundException();
        var messages = await repository.ListMessagesAsync(
            conversation.Id,
            cancellationToken);
        if (messages.Count > AiConversationOptions.MaximumMessages - 2)
        {
            throw new RequestValidationException(
                "ai.conversation.message_limit_reached",
                $"An AI conversation supports at most {AiConversationOptions.MaximumMessages} messages.");
        }

        var patientContext = conversation.PatientProfileId is { } patientProfileId
            ? await patientContextAssembler.AssembleAsync(patientProfileId, cancellationToken)
            : null;
        var userMessage = AiMessage.Create(
            conversation.Id,
            AiMessageRole.User,
            messageContent,
            messages.Count + 1,
            clock.UtcNow);
        var userView = ToView(userMessage);
        var preparedInput = contextBuilder.Build(
            messages.Append(userView).ToArray(),
            patientContext);
        var analysisRequest = AiAnalysisRequest.Create(
            current.AccountId,
            AiAnalysisPurpose.Conversation,
            "ai-conversation-input@v1",
            CreateInputProvenance(conversation, userMessage, patientContext),
            clock.UtcNow,
            conversation.PatientProfileId,
            conversation.Id);
        repository.Add(userMessage);
        repository.Add(analysisRequest);
        await repository.SaveChangesAsync(cancellationToken);

        var outcome = await safeExecution.ExecuteAsync(
            new ExecuteSafeAiAnalysisCommand(new ExecuteAiAnalysisCommand(
                analysisRequest.Id,
                AiWorkloadIdentifiers.Conversation,
                AiConversationContract.Prompt,
                preparedInput,
                AiConversationContract.Result,
                command.CorrelationIdentifier)),
            cancellationToken);

        AiConversationMessageView? assistantView = null;
        if (outcome.TechnicalOutcome == AiExecutionOutcomeKind.StructurallyValid &&
            !string.IsNullOrWhiteSpace(outcome.ResponseContent))
        {
            var visibleContent = outcome.ProviderOutputDisplayEligible
                ? ExtractApprovedAnswer(outcome.ResponseContent)
                : outcome.ResponseContent;
            var assistant = AiMessage.Create(
                conversation.Id,
                AiMessageRole.Assistant,
                visibleContent,
                userMessage.Sequence + 1,
                clock.UtcNow);
            repository.Add(assistant);
            await repository.SaveChangesAsync(CancellationToken.None);
            assistantView = ToView(assistant);
        }

        return new SendAiConversationMessageResult(
            conversation.Id,
            userMessage.Id,
            outcome.ExecutionId,
            MapState(outcome),
            assistantView);
    }

    private static string CreateInputProvenance(
        AiConversation conversation,
        AiMessage userMessage,
        AiPatientContext? patientContext) => JsonSerializer.Serialize(new
        {
            schemaVersion = "v1",
            conversationId = conversation.Id.Value,
            userMessageId = userMessage.Id.Value,
            patientContextSources = patientContext?.Sources.Select(source => new
            {
                source.SourceType,
                sourceId = source.SourceId.Value,
                source.OccurredAt
            }) ?? []
        });

    private static string ExtractApprovedAnswer(string structuredContent)
    {
        using var document = JsonDocument.Parse(structuredContent);
        return document.RootElement.GetProperty("answer").GetString()!;
    }

    private static AiConversationExecutionState MapState(AiSafeAnalysisOutcome outcome) =>
        outcome.TechnicalOutcome switch
        {
            AiExecutionOutcomeKind.StructurallyValid =>
                AiConversationExecutionState.Completed,
            AiExecutionOutcomeKind.MalformedResult =>
                AiConversationExecutionState.Rejected,
            _ => AiConversationExecutionState.Failed
        };

    internal static AiConversationMessageView ToView(AiMessage message) => new(
        message.Id,
        message.Role,
        message.Content,
        message.Sequence,
        message.CreatedAt);
}

internal static class AiConversationCursorCodec
{
    private const int Version = 1;

    public static string Encode(AiConversationPageCursor cursor)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(
            Version,
            cursor.AccountId.Value,
            cursor.CreatedAt,
            cursor.ConversationId.Value));
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static AiConversationPageCursor Decode(string value, EntityId accountId)
    {
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(
                normalized.Length + (4 - normalized.Length % 4) % 4,
                '=');
            var payload = JsonSerializer.Deserialize<CursorPayload>(
                Encoding.UTF8.GetString(Convert.FromBase64String(normalized)));
            if (payload is null || payload.Version != Version ||
                payload.AccountId != accountId.Value ||
                payload.ConversationId == Guid.Empty ||
                payload.CreatedAt.Offset != TimeSpan.Zero)
            {
                throw Invalid();
            }

            return new AiConversationPageCursor(
                accountId,
                payload.CreatedAt,
                EntityId.From(payload.ConversationId));
        }
        catch (Exception exception) when (exception is not RequestValidationException)
        {
            throw Invalid();
        }
    }

    public static RequestValidationException Invalid() => new(
        "ai.conversation.cursor_invalid",
        "The conversation cursor is invalid.");

    private sealed record CursorPayload(
        int Version,
        Guid AccountId,
        DateTimeOffset CreatedAt,
        Guid ConversationId);
}
