using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Api.Identity;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Beeexy.Api.Triage;

internal static class PreTriageEndpointExtensions
{
    internal const string AnonymousCapabilityHeader = "X-Pre-Triage-Capability";

    public static IEndpointRouteBuilder MapBeeexyPreTriageEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/pre-triage/sessions",
                StartSessionAsync)
            .WithName("StartPreTriageSession")
            .WithTags("Pre-Triage")
            .WithDescription(
                "Starts a temporary session for the explicit supported pathway. With no " +
                "Authorization header the session is anonymous and a capability is returned " +
                "once. With a valid Bearer token, patientId selects an authorized primary or " +
                "actively managed patient; omitting patientId selects the caller's primary " +
                "patient. An invalid supplied credential is never downgraded to anonymous.")
            .WithMetadata(new OptionalBearerAuthorizationMetadata())
            .Accepts<StartPreTriageSessionRequest>("application/json")
            .Produces<PreTriageSessionStartResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/pre-triage/sessions/{id:guid}/answers",
                SubmitAnswersAsync)
            .WithName("SubmitPreTriageAnswers")
            .WithTags("Pre-Triage")
            .WithDescription(
                "Submits either explicit structured demo answers or one natural-language " +
                "message (never both) and returns backend-authoritative questionnaire " +
                "progression. " +
                $"Anonymous sessions require {AnonymousCapabilityHeader}; authenticated " +
                "sessions require an authorized Bearer identity. Structured duration units " +
                "are MINUTES, HOURS, DAYS, WEEKS, or MONTHS; intensity is 1-10; additional " +
                "symptoms are NAUSEA, DIARRHEA, and FEVER when allowed by the pinned package; " +
                "FEVER is excluded when FEVER is the primary pathway. Natural-language " +
                "interpretation may " +
                "return a safe clarification or provider-unavailable outcome without writes.")
            .WithMetadata(new OptionalBearerAuthorizationMetadata())
            .Accepts<SubmitPreTriageAnswersRequest>("application/json")
            .Produces<PreTriageAnswerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> SubmitAnswersAsync(
        Guid id,
        SubmitPreTriageAnswersRequest request,
        HttpContext httpContext,
        SubmitTriageAnswers useCase,
        [FromHeader(Name = AnonymousCapabilityHeader)] string? anonymousCapability,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new PreTriageSessionNotFoundException();
        }

        var authorizationSupplied = httpContext.Request.Headers.ContainsKey(
            HeaderNames.Authorization);
        var authenticated = httpContext.User.Identity?.IsAuthenticated == true;
        if (authorizationSupplied && !authenticated)
        {
            throw new SessionAuthenticationException();
        }

        var result = await useCase.ExecuteAsync(
            new SubmitTriageAnswersCommand(
                EntityId.From(id),
                authenticated
                    ? PreTriageCallerMode.Authenticated
                    : PreTriageCallerMode.Anonymous,
                anonymousCapability,
                request.QuestionnaireVersion,
                request.Structured is null
                    ? null
                    : new StructuredTriageAnswerInput(
                        request.Structured.Duration is null
                            ? null
                            : new DurationTriageAnswerInput(
                                request.Structured.Duration.Value,
                                request.Structured.Duration.Unit,
                                request.Structured.Duration.UnsupportedFields?.Keys.ToArray() ?? []),
                        request.Structured.Intensity,
                        request.Structured.AdditionalSymptoms,
                        request.Structured.UnsupportedFields?.Keys.ToArray() ?? []),
                request.NaturalLanguage,
                request.UnsupportedFields?.Keys.ToArray() ?? []),
            cancellationToken);
        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> StartSessionAsync(
        StartPreTriageSessionRequest request,
        HttpContext httpContext,
        StartPreTriage useCase,
        CancellationToken cancellationToken)
    {
        var authorizationSupplied = httpContext.Request.Headers.ContainsKey(
            HeaderNames.Authorization);
        var authenticated = httpContext.User.Identity?.IsAuthenticated == true;
        if (authorizationSupplied && !authenticated)
        {
            throw new SessionAuthenticationException();
        }

        var result = await useCase.ExecuteAsync(
            new StartPreTriageCommand(
                request.Pathway,
                request.PatientId.HasValue
                    ? ParsePatientId(request.PatientId.Value)
                    : null,
                authenticated
                    ? PreTriageCallerMode.Authenticated
                    : PreTriageCallerMode.Anonymous,
                request.UnsupportedFields?.Keys.ToArray() ?? []),
            cancellationToken);

        return Results.Json(
            ToResponse(result),
            statusCode: StatusCodes.Status201Created);
    }

    private static EntityId ParsePatientId(Guid patientId)
    {
        if (patientId == Guid.Empty)
        {
            throw new PatientProfileNotFoundException();
        }

        return EntityId.From(patientId);
    }

    private static PreTriageSessionStartResponse ToResponse(StartPreTriageResult result) =>
        new(
            result.SessionId.Value,
            result.PatientProfileId?.Value,
            result.Pathway.Value,
            result.Status.ToString(),
            result.ExpiresAt,
            new ClinicalDefinitionReferenceResponse(
                result.QuestionnaireCode.Value,
                result.QuestionnaireVersion.Value),
            new ClinicalDefinitionReferenceResponse(
                result.RuleSetCode.Value,
                result.RuleSetVersion.Value),
            new ClinicalContentStatusResponse(
                ToApiValue(result.ClinicalContentStatus.Source),
                ToApiValue(result.ClinicalContentStatus.ReviewStatus),
                ToApiValue(result.ClinicalContentStatus.ApprovalStatus)),
            result.AnonymousCapability);

    private static PreTriageAnswerResponse ToResponse(SubmitTriageAnswersResult result) => new(
        result.SessionId.Value,
        result.Pathway.Value,
        result.QuestionnaireVersion.Value,
        ToApiEnum(result.Outcome),
        result.AcceptedAnswerCodes.Select(value => value.Value).ToArray(),
        new QuestionnaireProgressResponse(
            ToApiEnum(result.Progression.State),
            result.Progression.AnsweredRequiredFields.Select(value => value.Value).ToArray(),
            result.Progression.MissingRequiredFields.Select(value => value.Value).ToArray(),
            result.Progression.NextQuestion is null
                ? null
                : new NextQuestionResponse(
                    result.Progression.NextQuestion.Code.Value,
                    result.Progression.NextQuestion.Prompt,
                    ToApiEnum(result.Progression.NextQuestion.AnswerType),
                    result.Progression.NextQuestion.AllowedValues,
                    result.Progression.NextQuestion.AllowedUnits,
                    result.Progression.NextQuestion.Minimum,
                    result.Progression.NextQuestion.Maximum),
            result.Progression.ReadyToComplete),
        result.ClarificationCode is null
            ? null
            : new IntakeClarificationResponse(
                result.ClarificationCode,
                result.ClarificationClassification.HasValue
                    ? ToApiEnum(result.ClarificationClassification.Value)
                    : null));

    private static string ToApiEnum<T>(T value) where T : struct, Enum =>
        JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString());

    private static string ToApiValue(ClinicalContentSource value) => value switch
    {
        ClinicalContentSource.ReferencePlatformDerived => "REFERENCE_PLATFORM_DERIVED",
        ClinicalContentSource.LegacyUnspecified => "LEGACY_UNSPECIFIED",
        ClinicalContentSource.ProductDemoDefined => "PRODUCT_DEMO_DEFINED",
        _ => value.ToString().ToUpperInvariant()
    };

    private static string ToApiValue(ClinicalReviewStatus value) => value switch
    {
        ClinicalReviewStatus.Provisional => "PROVISIONAL",
        ClinicalReviewStatus.Reviewed => "REVIEWED",
        ClinicalReviewStatus.NotApplicable => "NOT_APPLICABLE",
        _ => value.ToString().ToUpperInvariant()
    };

    private static string ToApiValue(ClinicalApprovalStatus value) => value switch
    {
        ClinicalApprovalStatus.PendingFormalReview => "PENDING_FORMAL_REVIEW",
        ClinicalApprovalStatus.Approved => "APPROVED",
        ClinicalApprovalStatus.NotClinicallyApproved => "NOT_CLINICALLY_APPROVED",
        _ => value.ToString().ToUpperInvariant()
    };
}

internal sealed class StartPreTriageSessionRequest
{
    [StringLength(ClinicalPathwayCode.MaximumLength, MinimumLength = 1)]
    public string? Pathway { get; init; }

    public Guid? PatientId { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed class SubmitPreTriageAnswersRequest
{
    [StringLength(DefinitionVersion.MaximumLength, MinimumLength = 1)]
    public string? QuestionnaireVersion { get; init; }

    public StructuredPreTriageAnswersRequest? Structured { get; init; }

    [StringLength(SubmitTriageAnswers.MaximumNaturalLanguageLength, MinimumLength = 1)]
    public string? NaturalLanguage { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed class StructuredPreTriageAnswersRequest
{
    public DurationAnswerRequest? Duration { get; init; }

    public int? Intensity { get; init; }

    public IReadOnlyList<string>? AdditionalSymptoms { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed class DurationAnswerRequest
{
    public decimal Value { get; init; }

    public string? Unit { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed record PreTriageSessionStartResponse(
    Guid SessionId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PatientId,
    string Pathway,
    string Status,
    DateTimeOffset ExpiresAt,
    ClinicalDefinitionReferenceResponse Questionnaire,
    ClinicalDefinitionReferenceResponse RuleSet,
    ClinicalContentStatusResponse ClinicalContent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? AnonymousCapability);

internal sealed record ClinicalDefinitionReferenceResponse(
    string Code,
    string Version);

internal sealed record ClinicalContentStatusResponse(
    string Source,
    string ReviewStatus,
    string ClinicalApproval);

internal sealed record PreTriageAnswerResponse(
    Guid SessionId,
    string Pathway,
    string QuestionnaireVersion,
    string Outcome,
    IReadOnlyList<string> AcceptedAnswers,
    QuestionnaireProgressResponse Progression,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IntakeClarificationResponse? Clarification);

internal sealed record QuestionnaireProgressResponse(
    string State,
    IReadOnlyList<string> AnsweredRequiredFields,
    IReadOnlyList<string> MissingRequiredFields,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NextQuestionResponse? NextQuestion,
    bool ReadyToComplete);

internal sealed record NextQuestionResponse(
    string Code,
    string Prompt,
    string AnswerType,
    IReadOnlyList<string> AllowedValues,
    IReadOnlyList<string> AllowedUnits,
    decimal? Minimum,
    decimal? Maximum);

internal sealed record IntakeClarificationResponse(
    string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Classification);
