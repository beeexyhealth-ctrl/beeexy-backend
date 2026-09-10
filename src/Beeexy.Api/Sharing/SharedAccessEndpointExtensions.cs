using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Sharing;
using Microsoft.AspNetCore.Authorization;

namespace Beeexy.Api.Sharing;

internal static class SharedAccessEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexySharedAccessEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/shared-access/exchange", ExchangeAsync)
            .WithName("ExchangeShareCapability")
            .WithTags("Shared Access")
            .WithDescription(
                "Exchanges a reusable active share capability from the JSON body for a " +
                "short-lived, read-only token bound to one ShareGrant. No account login or " +
                "shared health data is involved.")
            .AllowAnonymous()
            .RequireRateLimiting(ShareExchangeRateLimiting.PolicyName)
            .Accepts<ExchangeShareCapabilityRequest>("application/json")
            .Produces<ExchangeShareCapabilityResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet("/api/v1/shared-access/profile", GetProfileAsync)
            .WithName("GetSharedProfile")
            .WithTags("Shared Access")
            .WithDescription(
                "Returns the canonical allow-listed health projection authorized by the " +
                "current active ShareGrant and its exact stored scope/items. The grant is " +
                "revalidated on every request and the credential is read-only.")
            .RequireAuthorization(new AuthorizeAttribute
            {
                Policy = ShareAccessAuthenticationDefaults.ReadOnlyPolicy,
                AuthenticationSchemes = ShareAccessAuthenticationDefaults.Scheme
            })
            .Produces<SharedProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        return endpoints;
    }

    private static async Task<IResult> ExchangeAsync(
        ExchangeShareCapabilityRequest request,
        HttpContext httpContext,
        ExchangeShareCapability useCase,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.Query.Count != 0 ||
            request.AdditionalFields is { Count: > 0 })
        {
            throw new ShareAccessDeniedException();
        }

        var result = await useCase.ExecuteAsync(request.Capability, cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new ExchangeShareCapabilityResponse(
            result.AccessToken,
            "Bearer",
            result.ExpiresAt));
    }

    private static async Task<IResult> GetProfileAsync(
        HttpContext httpContext,
        BuildSharedProfile useCase,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.Query.Count != 0 ||
            httpContext.Request.ContentLength is > 0 ||
            httpContext.Request.Headers.TransferEncoding.Count > 0 ||
            !ShareAccessAuthenticationDefaults.TryGetIdentity(
                httpContext.User,
                out var identity))
        {
            throw new ShareAccessDeniedException();
        }

        var result = await useCase.ExecuteAsync(identity, cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(ToResponse(result));
    }

    private static SharedProfileResponse ToResponse(BuildSharedProfileResult result)
    {
        var profile = result.Profile;
        return new SharedProfileResponse(
            result.Scope.ToString(),
            new SharedHealthProfileResponse(
                profile.Demographics is null
                    ? null
                    : new SharedPatientDemographicsResponse(
                        profile.Demographics.BeeexyId,
                        profile.Demographics.FirstName,
                        profile.Demographics.LastName,
                        profile.Demographics.DateOfBirth,
                        profile.Demographics.SexAssignedAtBirth,
                        profile.Demographics.State,
                        profile.Demographics.Version),
                profile.ClinicalHistory.Select(value =>
                    new SharedClinicalHistoryEventResponse(
                        value.EventId.Value,
                        value.EventType,
                        value.OccurredAt,
                        value.RecordedAt,
                        new SharedClinicalProvenanceResponse(
                            value.Provenance.SourceType,
                            value.Provenance.SourceId.Value,
                            value.Provenance.QuestionnaireVersionId.Value,
                            value.Provenance.ClinicalRuleSetVersionId.Value)))
                    .ToArray(),
                profile.PreTriage.Select(value =>
                    new SharedPreTriageRecordResponse(
                        value.EpisodeId.Value,
                        value.CompletedAt,
                        new SharedPreTriagePrimarySymptomResponse(
                            value.PrimarySymptom.Code,
                            value.PrimarySymptom.Display),
                        new SharedPreTriageDurationResponse(
                            value.Duration.Value,
                            value.Duration.Unit),
                        value.Intensity,
                        value.AdditionalSymptoms,
                        value.QuestionnaireVersionId.Value,
                        value.ClinicalRuleSetVersionId.Value))
                    .ToArray(),
                profile.SymptomDiaryEntries.Select(value =>
                    new SharedSymptomDiaryEntryResponse(
                        value.CheckInId.Value,
                        value.EpisodeId.Value,
                        value.RecordedAt,
                        value.Pathway,
                        ToResponse(value.Package),
                        value.Answers.Select(answer =>
                            new SharedSymptomDiaryAnswerResponse(
                                answer.QuestionCode,
                                answer.PromptText,
                                answer.Value))
                            .ToArray()))
                    .ToArray(),
                profile.SymptomDiaryContent.Select(value =>
                    new SharedSymptomDiaryContentResponse(
                        ToResponse(value.Package),
                        value.Pathway,
                        value.InformationalHeading,
                        value.InformationalBody,
                        value.WarningSigns.Select(warning =>
                            new SharedSymptomWarningSignResponse(
                                warning.Code,
                                warning.DisplayText,
                                warning.SourceOrder))
                            .ToArray()))
                    .ToArray(),
                profile.SecondOpinions.Select(value =>
                    new SharedSecondOpinionResultResponse(
                        value.ResultId.Value,
                        value.GeneratedAt,
                        value.ResultVersion,
                        value.Summary,
                        value.ImportantPoints,
                        value.PossibleQuestionsForDoctor,
                        value.MissingInformation,
                        value.Disclaimer))
                    .ToArray()));
    }

    private static SharedSymptomDiaryPackageVersionResponse ToResponse(
        SharedSymptomDiaryPackageVersion value) => new(
        value.PackageVersionId.Value,
        value.PackageCode,
        value.PackageVersion,
        value.CanonicalContentHash,
        value.QuestionSetCode,
        value.QuestionSetVersion,
        value.SymptomInformationCode,
        value.SymptomInformationVersion,
        value.ContentSource,
        value.ReviewStatus,
        value.ApprovalStatus,
        value.ApprovedAt);
}

internal sealed record ExchangeShareCapabilityRequest
{
    public string? Capability { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record ExchangeShareCapabilityResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt);

internal sealed record SharedProfileResponse(
    string Scope,
    SharedHealthProfileResponse Profile);

internal sealed record SharedHealthProfileResponse(
    SharedPatientDemographicsResponse? Demographics,
    IReadOnlyList<SharedClinicalHistoryEventResponse> ClinicalHistory,
    IReadOnlyList<SharedPreTriageRecordResponse> PreTriage,
    IReadOnlyList<SharedSymptomDiaryEntryResponse> SymptomDiaryEntries,
    IReadOnlyList<SharedSymptomDiaryContentResponse> SymptomDiaryContent,
    IReadOnlyList<SharedSecondOpinionResultResponse> SecondOpinions);

internal sealed record SharedPatientDemographicsResponse(
    string BeeexyId,
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    string? SexAssignedAtBirth,
    string? State,
    long Version);

internal sealed record SharedClinicalHistoryEventResponse(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    SharedClinicalProvenanceResponse Provenance);

internal sealed record SharedClinicalProvenanceResponse(
    string SourceType,
    Guid SourceId,
    Guid QuestionnaireVersionId,
    Guid ClinicalRuleSetVersionId);

internal sealed record SharedPreTriageRecordResponse(
    Guid EpisodeId,
    DateTimeOffset CompletedAt,
    SharedPreTriagePrimarySymptomResponse PrimarySymptom,
    SharedPreTriageDurationResponse Duration,
    int Intensity,
    IReadOnlyList<string> AdditionalSymptoms,
    Guid QuestionnaireVersionId,
    Guid ClinicalRuleSetVersionId);

internal sealed record SharedPreTriagePrimarySymptomResponse(string Code, string Display);

internal sealed record SharedPreTriageDurationResponse(decimal Value, string Unit);

internal sealed record SharedSymptomDiaryEntryResponse(
    Guid CheckInId,
    Guid EpisodeId,
    DateTimeOffset RecordedAt,
    string Pathway,
    SharedSymptomDiaryPackageVersionResponse Package,
    IReadOnlyList<SharedSymptomDiaryAnswerResponse> Answers);

internal sealed record SharedSymptomDiaryAnswerResponse(
    string QuestionCode,
    string PromptText,
    JsonElement Value);

internal sealed record SharedSymptomDiaryContentResponse(
    SharedSymptomDiaryPackageVersionResponse Package,
    string Pathway,
    string? InformationalHeading,
    string? InformationalBody,
    IReadOnlyList<SharedSymptomWarningSignResponse> WarningSigns);

internal sealed record SharedSymptomDiaryPackageVersionResponse(
    Guid PackageVersionId,
    string PackageCode,
    string PackageVersion,
    string CanonicalContentHash,
    string QuestionSetCode,
    string QuestionSetVersion,
    string SymptomInformationCode,
    string SymptomInformationVersion,
    string ContentSource,
    string ReviewStatus,
    string ApprovalStatus,
    DateTimeOffset ApprovedAt);

internal sealed record SharedSymptomWarningSignResponse(
    string Code,
    string DisplayText,
    int SourceOrder);

internal sealed record SharedSecondOpinionResultResponse(
    Guid ResultId,
    DateTimeOffset GeneratedAt,
    string ResultVersion,
    string Summary,
    IReadOnlyList<string> ImportantPoints,
    IReadOnlyList<string> PossibleQuestionsForDoctor,
    IReadOnlyList<string> MissingInformation,
    string Disclaimer);
