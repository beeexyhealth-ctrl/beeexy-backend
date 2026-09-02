using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Ai;
using Beeexy.Application.Common;
using Beeexy.Domain.Common;

namespace Beeexy.Api.Ai;

internal static class SecondOpinionEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexySecondOpinionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/ai/second-opinions", RequestAsync)
            .WithName("RequestSecondOpinion")
            .WithTags("AI Second Opinions")
            .WithDescription(
                "Starts one patient-authorized educational Second Opinion against explicit, " +
                "immutable text, zero-or-one active temporary document, Pre-Triage, and/or " +
                "Clinical History selections. It uses one provider execution and never creates " +
                "clinical, FHIR, directory, or scheduling side effects.")
            .RequireAuthorization()
            .Accepts<RequestSecondOpinionRequest>("application/json")
            .Produces<SecondOpinionAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet("/api/v1/ai/second-opinions/{id:guid}", GetAsync)
            .WithName("GetSecondOpinion")
            .WithTags("AI Second Opinions")
            .WithDescription(
                "Returns only owner-visible, currently patient-authorized execution status and " +
                "an approved immutable result or fixed Beeexy safety message. Rejected provider " +
                "output is never returned and source documents are never reread.")
            .RequireAuthorization()
            .Produces<SecondOpinionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/ai/second-opinions/{id:guid}/regenerate",
                RegenerateAsync)
            .WithName("RegenerateSecondOpinion")
            .WithTags("AI Second Opinions")
            .WithDescription(
                "Creates one independently traceable execution from the original immutable " +
                "Second Opinion input. An approved execution appends a new result snapshot; " +
                "later patient or document changes are not read, and prior results are never " +
                "replaced by failed or rejected executions.")
            .RequireAuthorization()
            .Produces<SecondOpinionAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> RequestAsync(
        RequestSecondOpinionRequest request,
        HttpContext httpContext,
        RequestSecondOpinion useCase,
        CancellationToken cancellationToken)
    {
        if (request.AdditionalFields is { Count: > 0 })
        {
            throw new RequestValidationException(
                "ai.second_opinion.unsupported_field",
                "The Second Opinion request contains an unsupported field.");
        }

        if (request.PatientId == Guid.Empty ||
            request.DocumentIds?.Any(id => id == Guid.Empty) == true ||
            request.PreTriageSessionId == Guid.Empty ||
            request.ClinicalHistoryEventIds?.Any(id => id == Guid.Empty) == true)
        {
            throw new RequestValidationException(
                "ai.second_opinion.source_ids_invalid",
                "Patient and selected source identifiers must be non-empty UUIDs.");
        }

        var result = await useCase.ExecuteAsync(
            new RequestSecondOpinionCommand(
                EntityId.From(request.PatientId),
                request.Text,
                request.DocumentIds?.Select(EntityId.From).ToArray(),
                request.PreTriageSessionId.HasValue
                    ? EntityId.From(request.PreTriageSessionId.Value)
                    : null,
                request.ClinicalHistoryEventIds?.Select(EntityId.From).ToArray(),
                httpContext.TraceIdentifier),
            cancellationToken);
        var location = $"/api/v1/ai/second-opinions/{result.AnalysisId.Value:D}";
        return Results.Accepted(
            location,
            new SecondOpinionAcceptedResponse(
                result.AnalysisId.Value,
                result.ExecutionId.Value,
                Status(result.Status),
                location));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetSecondOpinion useCase,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new SecondOpinionNotFoundException();
        }

        return Results.Ok(ToResponse(
            await useCase.ExecuteAsync(EntityId.From(id), cancellationToken)));
    }

    private static async Task<IResult> RegenerateAsync(
        Guid id,
        HttpContext httpContext,
        RegenerateSecondOpinion useCase,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new SecondOpinionNotFoundException();
        }

        if (httpContext.Request.ContentLength is > 0 ||
            httpContext.Request.Headers.TransferEncoding.Count > 0)
        {
            throw new RequestValidationException(
                "ai.second_opinion.regeneration_body_not_allowed",
                "Second Opinion regeneration does not accept replacement input.");
        }

        var result = await useCase.ExecuteAsync(
            new RegenerateSecondOpinionCommand(
                EntityId.From(id),
                httpContext.TraceIdentifier),
            cancellationToken);
        var location = $"/api/v1/ai/second-opinions/{result.AnalysisId.Value:D}";
        return Results.Accepted(
            location,
            new SecondOpinionAcceptedResponse(
                result.AnalysisId.Value,
                result.ExecutionId.Value,
                Status(result.Status),
                location));
    }

    private static SecondOpinionResponse ToResponse(SecondOpinionDetail value) => new(
        value.AnalysisId.Value,
        value.PatientProfileId.Value,
        value.ExecutionId?.Value,
        Status(value.Status),
        value.Result is null
            ? null
            : new SecondOpinionResultResponse(
                value.Result.Summary,
                value.Result.ImportantPoints,
                value.Result.PossibleQuestionsForDoctor,
                value.Result.MissingInformation,
                value.Result.Disclaimer),
        value.Metadata is null
            ? null
            : new SecondOpinionMetadataResponse(
                value.Metadata.AiGenerated,
                value.Metadata.GeneratedAt,
                value.Metadata.ResultVersion,
                value.Metadata.Provider,
                value.Metadata.ModelVersion,
                value.Metadata.PromptVersion,
                value.Metadata.DisclaimerVersion),
        value.SafeMessage);

    private static string Status(SecondOpinionStatus value) => value switch
    {
        SecondOpinionStatus.Pending => "pending",
        SecondOpinionStatus.Running => "running",
        SecondOpinionStatus.Succeeded => "succeeded",
        SecondOpinionStatus.Failed => "failed",
        SecondOpinionStatus.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

internal sealed record RequestSecondOpinionRequest(
    Guid PatientId,
    [property: StringLength(SecondOpinionOptions.MaximumTypedTextCharacters)] string? Text,
    IReadOnlyList<Guid>? DocumentIds,
    Guid? PreTriageSessionId,
    IReadOnlyList<Guid>? ClinicalHistoryEventIds)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record SecondOpinionAcceptedResponse(
    Guid AnalysisId,
    Guid ExecutionId,
    string Status,
    string StatusUrl);

internal sealed record SecondOpinionResultResponse(
    string Summary,
    IReadOnlyList<string> ImportantPoints,
    IReadOnlyList<string> PossibleQuestionsForDoctor,
    IReadOnlyList<string> MissingInformation,
    string Disclaimer);

internal sealed record SecondOpinionMetadataResponse(
    bool AiGenerated,
    DateTimeOffset GeneratedAt,
    string ResultVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Provider,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ModelVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PromptVersion,
    string DisclaimerVersion);

internal sealed record SecondOpinionResponse(
    Guid AnalysisId,
    Guid PatientId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ExecutionId,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SecondOpinionResultResponse? Result,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SecondOpinionMetadataResponse? Metadata,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SafeMessage);
