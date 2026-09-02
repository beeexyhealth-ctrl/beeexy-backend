using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Ai;
using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Api.Ai;

internal static class AiConversationEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexyAiConversationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/ai/conversations", CreateAsync)
            .WithName("CreateAiConversation")
            .WithTags("AI Conversations")
            .WithDescription(
                "Creates an authenticated, account-owned informational health conversation. " +
                "An optional patient association requires current patient authority. Creation " +
                "does not invoke the AI provider.")
            .RequireAuthorization()
            .Accepts<CreateAiConversationRequest>("application/json")
            .Produces<AiConversationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet("/api/v1/ai/conversations", ListAsync)
            .WithName("ListAiConversations")
            .WithTags("AI Conversations")
            .WithDescription(
                "Lists the authenticated account's non-deleted AI History in deterministic " +
                "newest-first order using opaque cursor pagination.")
            .RequireAuthorization()
            .Produces<AiConversationPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet("/api/v1/ai/conversations/{id:guid}", GetAsync)
            .WithName("GetAiConversation")
            .WithTags("AI Conversations")
            .WithDescription(
                "Returns owner-visible AI History in message-sequence order. Missing, foreign, " +
                "and logically deleted conversations return the same concealed 404.")
            .RequireAuthorization()
            .Produces<AiConversationDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost("/api/v1/ai/conversations/{id:guid}/messages", SendMessageAsync)
            .WithName("SendAiConversationMessage")
            .WithTags("AI Conversations")
            .WithDescription(
                "Accepts one health-information message for execution through the single " +
                "configured provider, structural validation, and Beeexy safety validation. " +
                "A conversation supports at most 50 total user and assistant messages.")
            .RequireAuthorization()
            .Accepts<SendAiConversationMessageRequest>("application/json")
            .Produces<AiConversationExecutionResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapDelete("/api/v1/ai/conversations/{id:guid}", DeleteAsync)
            .WithName("DeleteAiConversation")
            .WithTags("AI Conversations")
            .WithDescription(
                "Logically deletes an owner-visible AI conversation. Repeating deletion by the " +
                "owner is idempotent; history and audit artifacts are retained internally.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateAiConversationRequest request,
        CreateAiConversation useCase,
        AiSafetyProductContent productContent,
        CancellationToken cancellationToken)
    {
        RejectUnsupportedFields(request.AdditionalFields);
        if (request.PatientId == Guid.Empty)
        {
            throw new PatientProfileNotFoundException();
        }

        var result = await useCase.ExecuteAsync(
            new CreateAiConversationCommand(
                request.Purpose,
                request.PatientId.HasValue
                    ? EntityId.From(request.PatientId.Value)
                    : null),
            cancellationToken);
        return Results.Created(
            $"/api/v1/ai/conversations/{result.ConversationId.Value:D}",
            ToResponse(result, productContent));
    }

    private static async Task<IResult> ListAsync(
        string? cursor,
        int? pageSize,
        ListAiConversations useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new ListAiConversationsQuery(cursor, pageSize),
            cancellationToken);
        return Results.Ok(new AiConversationPageResponse(
            result.Items.Select(ToSummaryResponse).ToArray(),
            result.NextCursor));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetAiConversation useCase,
        AiSafetyProductContent productContent,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new AiConversationNotFoundException();
        }

        var result = await useCase.ExecuteAsync(EntityId.From(id), cancellationToken);
        return Results.Ok(new AiConversationDetailResponse(
            ToSummaryResponse(result.Conversation),
            result.Messages.Select(ToResponse).ToArray(),
            Disclaimer(productContent)));
    }

    private static async Task<IResult> SendMessageAsync(
        Guid id,
        SendAiConversationMessageRequest request,
        HttpContext httpContext,
        SendAiConversationMessage useCase,
        AiSafetyProductContent productContent,
        CancellationToken cancellationToken)
    {
        RejectUnsupportedFields(request.AdditionalFields);
        if (id == Guid.Empty)
        {
            throw new AiConversationNotFoundException();
        }

        var result = await useCase.ExecuteAsync(
            new SendAiConversationMessageCommand(
                EntityId.From(id),
                request.Content,
                httpContext.TraceIdentifier),
            cancellationToken);
        return Results.Accepted(
            $"/api/v1/ai/conversations/{id:D}",
            new AiConversationExecutionResponse(
                result.ConversationId.Value,
                result.UserMessageId.Value,
                result.ExecutionId.Value,
                result.State switch
                {
                    AiConversationExecutionState.Completed => "completed",
                    AiConversationExecutionState.Rejected => "rejected",
                    _ => "failed"
                },
                result.AssistantMessage is null ? null : ToResponse(result.AssistantMessage),
                Disclaimer(productContent)));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        DeleteAiConversation useCase,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new AiConversationNotFoundException();
        }

        await useCase.ExecuteAsync(EntityId.From(id), cancellationToken);
        return Results.NoContent();
    }

    private static void RejectUnsupportedFields(
        IDictionary<string, JsonElement>? fields)
    {
        if (fields is { Count: > 0 })
        {
            throw new RequestValidationException(
                "ai.conversation.unsupported_field",
                "The AI conversation request contains an unsupported field.");
        }
    }

    private static AiConversationResponse ToResponse(
        AiConversationSummary conversation,
        AiSafetyProductContent productContent) => new(
        conversation.ConversationId.Value,
        conversation.PatientProfileId?.Value,
        conversation.CreatedAt,
        Disclaimer(productContent));

    private static AiConversationSummaryResponse ToSummaryResponse(
        AiConversationSummary conversation) => new(
        conversation.ConversationId.Value,
        conversation.PatientProfileId?.Value,
        conversation.CreatedAt);

    private static AiConversationMessageResponse ToResponse(
        AiConversationMessageView message) => new(
        message.MessageId.Value,
        message.Role == AiMessageRole.User ? "user" : "assistant",
        message.Content,
        message.Sequence,
        message.CreatedAt);

    private static AiDisclaimerResponse Disclaimer(AiSafetyProductContent content) => new(
        content.DisclaimerVersion,
        content.Disclaimer);
}

internal sealed record CreateAiConversationRequest(
    [property: Required] string? Purpose,
    Guid? PatientId)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record SendAiConversationMessageRequest(
    [property: Required, StringLength(AiConversationOptions.MaximumMessageCharacters)]
    string? Content)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record AiDisclaimerResponse(string Version, string Content);

internal sealed record AiConversationResponse(
    Guid ConversationId,
    Guid? PatientId,
    DateTimeOffset CreatedAt,
    AiDisclaimerResponse Disclaimer);

internal sealed record AiConversationSummaryResponse(
    Guid ConversationId,
    Guid? PatientId,
    DateTimeOffset CreatedAt);

internal sealed record AiConversationPageResponse(
    IReadOnlyList<AiConversationSummaryResponse> Items,
    string? NextCursor);

internal sealed record AiConversationMessageResponse(
    Guid MessageId,
    string Role,
    string Content,
    int Sequence,
    DateTimeOffset CreatedAt);

internal sealed record AiConversationDetailResponse(
    AiConversationSummaryResponse Conversation,
    IReadOnlyList<AiConversationMessageResponse> Messages,
    AiDisclaimerResponse Disclaimer);

internal sealed record AiConversationExecutionResponse(
    Guid ConversationId,
    Guid UserMessageId,
    Guid ExecutionId,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AiConversationMessageResponse? AssistantMessage,
    AiDisclaimerResponse Disclaimer);
