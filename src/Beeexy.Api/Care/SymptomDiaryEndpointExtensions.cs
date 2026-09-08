using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Care;
using Beeexy.Application.Common;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.AspNetCore.Mvc;

namespace Beeexy.Api.Care;

internal static class SymptomDiaryEndpointExtensions
{
    private static readonly HashSet<string> HistoryQueryParameters =
        ["cursor", "pageSize"];

    internal const string ContentRoute =
        "/api/v1/pre-triage/episodes/{episodeId:guid}/symptom-diary-content";
    internal const string CheckInRoute =
        "/api/v1/pre-triage/episodes/{episodeId:guid}/check-ins";

    public static IEndpointRouteBuilder MapBeeexySymptomDiaryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ContentRoute, GetContentAsync)
            .WithName("GetSymptomDiaryContent")
            .WithTags("Symptom Diary")
            .WithDescription(
                "Returns the active reviewed symptom-diary questions, ordered options, and " +
                "separate static warning-sign information for an eligible completed " +
                "patient-owned Pre-Triage episode. The pathway is derived only from the " +
                "episode's frozen questionnaire. Missing or inaccessible episodes are " +
                "concealed as 404, and unavailable approved content returns 422.")
            .RequireAuthorization()
            .Produces<SymptomDiaryContentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(CheckInRoute, RecordCheckInAsync)
            .WithName("RecordSymptomCheckIn")
            .WithTags("Symptom Diary")
            .WithDescription(
                "Voluntarily records one immutable symptom-diary entry against an eligible " +
                "completed patient-owned Pre-Triage episode. The body supplies a non-empty " +
                "UUID idempotencyKey, the exact immutable packageVersionId previously " +
                "presented, and structurally validated answers. First creation returns 201; " +
                "an identical retry returns 200; incompatible key reuse returns 409. The " +
                "operation performs no clinical interpretation or downstream action.")
            .RequireAuthorization()
            .Produces<SymptomCheckInResponse>(StatusCodes.Status201Created)
            .Produces<SymptomCheckInResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet(CheckInRoute, ListCheckInsAsync)
            .WithName("ListSymptomCheckIns")
            .WithTags("Symptom Diary")
            .WithDescription(
                "Returns an oldest-first opaque-cursor page of immutable symptom-diary " +
                "entries for an eligible patient-owned Pre-Triage episode. Each entry is " +
                "reconstructed only from its own exact frozen package version; no trend, " +
                "comparison, clinical interpretation, or current-content substitution is " +
                "performed. Page size defaults to 20 and is limited to 100.")
            .RequireAuthorization()
            .Produces<SymptomCheckInHistoryPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> GetContentAsync(
        Guid episodeId,
        HttpRequest request,
        GetSymptomDiaryContent useCase,
        CancellationToken cancellationToken)
    {
        if (episodeId == Guid.Empty)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        if (request.Query.Count != 0)
        {
            throw new RequestValidationException(
                "symptom_diary.unsupported_query",
                "Symptom-diary content retrieval does not accept query parameters.");
        }

        if (request.ContentLength is > 0 || request.Headers.TransferEncoding.Count > 0)
        {
            throw new RequestValidationException(
                "symptom_diary.unsupported_body",
                "Symptom-diary content retrieval does not accept a request body.");
        }

        var result = await useCase.ExecuteAsync(
            EntityId.From(episodeId),
            cancellationToken);
        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> RecordCheckInAsync(
        Guid episodeId,
        HttpRequest httpRequest,
        RecordSymptomCheckInRequest request,
        RecordSymptomCheckIn useCase,
        CancellationToken cancellationToken)
    {
        if (episodeId == Guid.Empty)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        if (httpRequest.Query.Count != 0)
        {
            throw new RequestValidationException(
                "symptom_diary.unsupported_query",
                "Symptom-diary check-in creation does not accept query parameters.");
        }

        var answers = request.Answers?.Select(answer =>
            answer is null
                ? new SymptomDiarySubmittedAnswer(null, default, HasUnsupportedFields: true)
                : new SymptomDiarySubmittedAnswer(
                    answer.QuestionCode,
                    answer.Value,
                    answer.AdditionalFields is { Count: > 0 }))
            .ToArray() ?? [];
        var result = await useCase.ExecuteAsync(
            new RecordSymptomCheckInCommand(
                EntityId.From(episodeId),
                EntityId.From(request.PackageVersionId),
                EntityId.From(request.IdempotencyKey),
                answers,
                AnswersProvided: request.Answers is not null,
                HasUnsupportedFields: request.AdditionalFields is { Count: > 0 }),
            cancellationToken);
        var response = ToResponse(result);
        return result.NewlyCreated
            ? Results.Json(response, statusCode: StatusCodes.Status201Created)
            : Results.Ok(response);
    }

    private static async Task<IResult> ListCheckInsAsync(
        Guid episodeId,
        [FromQuery(Name = "cursor")] string? cursorQuery,
        [FromQuery(Name = "pageSize")] string? pageSizeQuery,
        HttpRequest request,
        ListSymptomCheckIns useCase,
        CancellationToken cancellationToken)
    {
        if (episodeId == Guid.Empty)
        {
            throw new SymptomDiaryEpisodeNotFoundException();
        }

        var hasUnsupportedQuery =
            request.Query.Keys.Any(key => !HistoryQueryParameters.Contains(key)) ||
            request.Query.Any(parameter => parameter.Value.Count > 1);
        var hasUnsupportedBody =
            request.ContentLength is > 0 || request.Headers.TransferEncoding.Count > 0;

        int? pageSize = null;
        var hasInvalidPageSize = false;
        if (pageSizeQuery is not null)
        {
            if (!int.TryParse(pageSizeQuery, out var parsedPageSize))
            {
                hasInvalidPageSize = true;
            }
            else
            {
                pageSize = parsedPageSize;
            }
        }

        var result = await useCase.ExecuteAsync(
            new ListSymptomCheckInsQuery(
                EntityId.From(episodeId),
                cursorQuery,
                pageSize,
                hasUnsupportedQuery,
                hasUnsupportedBody,
                hasInvalidPageSize),
            cancellationToken);
        return Results.Ok(new SymptomCheckInHistoryPageResponse(
            result.Items.Select(ToResponse).ToArray(),
            result.NextCursor));
    }

    private static SymptomDiaryContentResponse ToResponse(
        SymptomDiaryContentForEpisode result)
    {
        var package = result.Package;
        var definition = package.Definition;
        return new SymptomDiaryContentResponse(
            result.EpisodeId.Value,
            result.Pathway.Value,
            package.PackageVersionId.Value,
            definition.PackageCode.Value,
            definition.PackageVersion.Value,
            package.CanonicalContentHash.Value,
            new SymptomDiaryContentProvenanceResponse(
                ToApiValue(definition.ContentStatus.Source),
                ToApiValue(definition.ContentStatus.ReviewStatus),
                ToApiValue(definition.ContentStatus.ApprovalStatus),
                definition.ApprovedAt!.Value),
            new SymptomDiaryQuestionSetResponse(
                definition.QuestionSetCode.Value,
                definition.QuestionSetVersion.Value,
                definition.Questions.Select(question =>
                    new SymptomDiaryQuestionResponse(
                        question.Code.Value,
                        question.PromptText,
                        question.SourceOrder,
                        question.IsRequired,
                        JsonSerializer.Deserialize<JsonElement>(question.AnswerSchemaJson),
                        question.Options.Select(option =>
                            new SymptomDiaryQuestionOptionResponse(
                                option.Code.Value,
                                option.Value,
                                option.DisplayText,
                                option.SourceOrder))
                            .ToArray()))
                    .ToArray()),
            new SymptomDiaryInformationResponse(
                definition.SymptomInformationCode.Value,
                definition.SymptomInformationVersion.Value,
                definition.InformationalHeading,
                definition.InformationalBody,
                definition.WarningSigns.Select(warning =>
                    new SymptomDiaryWarningSignResponse(
                        warning.Code.Value,
                        warning.DisplayText,
                        warning.SourceOrder))
                    .ToArray()));
    }

    private static SymptomCheckInResponse ToResponse(RecordSymptomCheckInResult result)
    {
        var definition = result.Package.Definition;
        return new SymptomCheckInResponse(
            result.CheckInId.Value,
            result.EpisodeId.Value,
            result.CreatedAt,
            result.Pathway.Value,
            result.PackageVersionId.Value,
            definition.PackageCode.Value,
            definition.PackageVersion.Value,
            result.Package.CanonicalContentHash.Value,
            new SymptomDiaryContentProvenanceResponse(
                ToApiValue(definition.ContentStatus.Source),
                ToApiValue(definition.ContentStatus.ReviewStatus),
                ToApiValue(definition.ContentStatus.ApprovalStatus),
                definition.ApprovedAt!.Value),
            new SymptomDiaryCheckInQuestionSetResponse(
                definition.QuestionSetCode.Value,
                definition.QuestionSetVersion.Value),
            result.Answers.Select(answer =>
                new SymptomDiaryAcceptedAnswerResponse(
                    answer.Question.Code.Value,
                    answer.Question.PromptText,
                    answer.Question.SourceOrder,
                    answer.Question.IsRequired,
                    JsonSerializer.Deserialize<JsonElement>(
                        answer.Question.AnswerSchemaJson),
                    answer.Question.Options.Select(option =>
                        new SymptomDiaryQuestionOptionResponse(
                            option.Code.Value,
                            option.Value,
                            option.DisplayText,
                            option.SourceOrder))
                        .ToArray(),
                    answer.Value))
                .ToArray(),
            new SymptomDiaryInformationResponse(
                definition.SymptomInformationCode.Value,
                definition.SymptomInformationVersion.Value,
                definition.InformationalHeading,
                definition.InformationalBody,
                definition.WarningSigns.Select(warning =>
                    new SymptomDiaryWarningSignResponse(
                        warning.Code.Value,
                        warning.DisplayText,
                        warning.SourceOrder))
                    .ToArray()));
    }

    private static SymptomCheckInResponse ToResponse(SymptomCheckInHistoryItem result)
    {
        var definition = result.Package.Definition;
        return new SymptomCheckInResponse(
            result.CheckInId.Value,
            result.EpisodeId.Value,
            result.CreatedAt,
            result.Pathway.Value,
            result.Package.PackageVersionId.Value,
            definition.PackageCode.Value,
            definition.PackageVersion.Value,
            result.Package.CanonicalContentHash.Value,
            new SymptomDiaryContentProvenanceResponse(
                ToApiValue(definition.ContentStatus.Source),
                ToApiValue(definition.ContentStatus.ReviewStatus),
                ToApiValue(definition.ContentStatus.ApprovalStatus),
                definition.ApprovedAt!.Value),
            new SymptomDiaryCheckInQuestionSetResponse(
                definition.QuestionSetCode.Value,
                definition.QuestionSetVersion.Value),
            result.Answers.Select(answer =>
                new SymptomDiaryAcceptedAnswerResponse(
                    answer.Question.Code.Value,
                    answer.Question.PromptText,
                    answer.Question.SourceOrder,
                    answer.Question.IsRequired,
                    JsonSerializer.Deserialize<JsonElement>(
                        answer.Question.AnswerSchemaJson),
                    answer.Question.Options.Select(option =>
                        new SymptomDiaryQuestionOptionResponse(
                            option.Code.Value,
                            option.Value,
                            option.DisplayText,
                            option.SourceOrder))
                        .ToArray(),
                    answer.Value))
                .ToArray(),
            new SymptomDiaryInformationResponse(
                definition.SymptomInformationCode.Value,
                definition.SymptomInformationVersion.Value,
                definition.InformationalHeading,
                definition.InformationalBody,
                definition.WarningSigns.Select(warning =>
                    new SymptomDiaryWarningSignResponse(
                        warning.Code.Value,
                        warning.DisplayText,
                        warning.SourceOrder))
                    .ToArray()));
    }

    private static string ToApiValue(ClinicalContentSource source) => source switch
    {
        ClinicalContentSource.MedicalTeamProvided => "MEDICAL_TEAM_PROVIDED",
        _ => throw new SymptomDiaryContentUnavailableException()
    };

    private static string ToApiValue(ClinicalReviewStatus status) => status switch
    {
        ClinicalReviewStatus.Reviewed => "REVIEWED",
        _ => throw new SymptomDiaryContentUnavailableException()
    };

    private static string ToApiValue(ClinicalApprovalStatus status) => status switch
    {
        ClinicalApprovalStatus.Approved => "APPROVED",
        _ => throw new SymptomDiaryContentUnavailableException()
    };
}

internal sealed record SymptomDiaryContentResponse(
    Guid EpisodeId,
    string Pathway,
    Guid PackageVersionId,
    string PackageCode,
    string PackageVersion,
    string ContentHash,
    SymptomDiaryContentProvenanceResponse Provenance,
    SymptomDiaryQuestionSetResponse QuestionSet,
    SymptomDiaryInformationResponse Information);

internal sealed record SymptomDiaryContentProvenanceResponse(
    string Source,
    string ReviewStatus,
    string ApprovalStatus,
    DateTimeOffset ApprovedAt);

internal sealed record SymptomDiaryQuestionSetResponse(
    string Code,
    string Version,
    IReadOnlyList<SymptomDiaryQuestionResponse> Questions);

internal sealed record SymptomDiaryQuestionResponse(
    string Code,
    string Prompt,
    int SourceOrder,
    bool IsRequired,
    JsonElement AnswerSchema,
    IReadOnlyList<SymptomDiaryQuestionOptionResponse> Options);

internal sealed record SymptomDiaryQuestionOptionResponse(
    string Code,
    string Value,
    string DisplayText,
    int SourceOrder);

internal sealed record SymptomDiaryInformationResponse(
    string Code,
    string Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Heading,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body,
    IReadOnlyList<SymptomDiaryWarningSignResponse> WarningSigns);

internal sealed record SymptomDiaryWarningSignResponse(
    string Code,
    string DisplayText,
    int SourceOrder);

internal sealed record RecordSymptomCheckInRequest
{
    public Guid PackageVersionId { get; init; }

    public Guid IdempotencyKey { get; init; }

    public IReadOnlyList<SymptomDiarySubmittedAnswerRequest?>? Answers { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record SymptomDiarySubmittedAnswerRequest
{
    public string? QuestionCode { get; init; }

    public JsonElement Value { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record SymptomCheckInResponse(
    Guid CheckInId,
    Guid EpisodeId,
    DateTimeOffset CreatedAt,
    string Pathway,
    Guid PackageVersionId,
    string PackageCode,
    string PackageVersion,
    string ContentHash,
    SymptomDiaryContentProvenanceResponse Provenance,
    SymptomDiaryCheckInQuestionSetResponse QuestionSet,
    IReadOnlyList<SymptomDiaryAcceptedAnswerResponse> Answers,
    SymptomDiaryInformationResponse Information);

internal sealed record SymptomDiaryCheckInQuestionSetResponse(
    string Code,
    string Version);

internal sealed record SymptomDiaryAcceptedAnswerResponse(
    string QuestionCode,
    string Prompt,
    int SourceOrder,
    bool IsRequired,
    JsonElement AnswerSchema,
    IReadOnlyList<SymptomDiaryQuestionOptionResponse> Options,
    JsonElement Value);

internal sealed record SymptomCheckInHistoryPageResponse(
    IReadOnlyList<SymptomCheckInResponse> Items,
    string? NextCursor);
