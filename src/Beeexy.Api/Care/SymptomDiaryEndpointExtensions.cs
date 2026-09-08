using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Care;
using Beeexy.Application.Common;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Api.Care;

internal static class SymptomDiaryEndpointExtensions
{
    internal const string ContentRoute =
        "/api/v1/pre-triage/episodes/{episodeId:guid}/symptom-diary-content";

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
