using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Api.Identity;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.Net.Http.Headers;

namespace Beeexy.Api.Triage;

internal static class PreTriageEndpointExtensions
{
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

        return endpoints;
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

    private static string ToApiValue(ClinicalContentSource value) => value switch
    {
        ClinicalContentSource.ReferencePlatformDerived => "REFERENCE_PLATFORM_DERIVED",
        ClinicalContentSource.LegacyUnspecified => "LEGACY_UNSPECIFIED",
        _ => value.ToString().ToUpperInvariant()
    };

    private static string ToApiValue(ClinicalReviewStatus value) => value switch
    {
        ClinicalReviewStatus.Provisional => "PROVISIONAL",
        ClinicalReviewStatus.Reviewed => "REVIEWED",
        _ => value.ToString().ToUpperInvariant()
    };

    private static string ToApiValue(ClinicalApprovalStatus value) => value switch
    {
        ClinicalApprovalStatus.PendingFormalReview => "PENDING_FORMAL_REVIEW",
        ClinicalApprovalStatus.Approved => "APPROVED",
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
