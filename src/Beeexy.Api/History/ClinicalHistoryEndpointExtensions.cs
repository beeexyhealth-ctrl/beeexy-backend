using Beeexy.Application.History;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beeexy.Api.History;

internal static class ClinicalHistoryEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexyClinicalHistoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/patients/{patientId:guid}/clinical-history",
                ListClinicalHistoryAsync)
            .WithName("ListClinicalHistory")
            .WithTags("Clinical History")
            .WithDescription(
                "Returns projected Clinical History events for an authorized primary or " +
                "actively managed patient using opaque cursor pagination (default page size " +
                "20, maximum 100). The optional eventType filter currently accepts only " +
                "COMPLETED_PRE_TRIAGE. Absent and " +
                "unauthorized patients both return a concealed 404. Events inserted after " +
                "the first page are evaluated against the keyset boundary: newer events are " +
                "excluded while older events remain eligible.")
            .RequireAuthorization()
            .Produces<ClinicalHistoryPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/pre-triage/episodes/{episodeId:guid}/amendments",
                AmendPreTriageEpisodeAsync)
            .WithName("AmendPreTriageEpisode")
            .WithTags("Clinical History")
            .WithDescription(
                "Adds an immutable, traceable amendment to an eligible completed " +
                "Pre-Triage episode for its primary patient or active manager. The current " +
                "Phase 5 amendment model records reason and server-controlled audit " +
                "metadata; it does not accept a clinical patch or overwrite the original. " +
                "A non-empty UUID idempotencyKey is required, and repeating it for the " +
                "same event returns 409. Absent and inaccessible sources both return a " +
                "concealed 404.")
            .RequireAuthorization()
            .Produces<ClinicalHistoryAmendmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet(
                "/api/v1/patients/{patientId:guid}/clinical-history/{eventId:guid}",
                GetClinicalHistoryEventAsync)
            .WithName("GetClinicalHistoryEvent")
            .WithTags("Clinical History")
            .WithDescription(
                "Returns one Clinical History event for an authorized primary or actively " +
                "managed patient, including its frozen authoritative Pre-Triage source " +
                "provenance, immutable neutral intake summary, and existing amendments in " +
                "oldest-to-newest order. The original source remains immutable. Absent " +
                "patients, inaccessible patients, absent events, and events belonging to " +
                "another patient all return a concealed 404.")
            .RequireAuthorization()
            .Produces<ClinicalHistoryEventDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> ListClinicalHistoryAsync(
        Guid patientId,
        string? cursor,
        int? pageSize,
        string? eventType,
        ListClinicalHistory useCase,
        CancellationToken cancellationToken)
    {
        if (patientId == Guid.Empty)
        {
            throw new PatientProfileNotFoundException();
        }

        var result = await useCase.ExecuteAsync(
            new ListClinicalHistoryQuery(
                EntityId.From(patientId),
                cursor,
                pageSize,
                eventType),
            cancellationToken);
        return Results.Ok(new ClinicalHistoryPageResponse(
            result.Items.Select(ToResponse).ToArray(),
            result.NextCursor));
    }

    private static async Task<IResult> GetClinicalHistoryEventAsync(
        Guid patientId,
        Guid eventId,
        GetClinicalHistoryEvent useCase,
        CancellationToken cancellationToken)
    {
        if (patientId == Guid.Empty || eventId == Guid.Empty)
        {
            throw new PatientProfileNotFoundException();
        }

        var result = await useCase.ExecuteAsync(
            EntityId.From(patientId),
            EntityId.From(eventId),
            cancellationToken);
        var item = ToResponse(result.Event);
        var summary = result.PreTriageSummary;
        return Results.Ok(new ClinicalHistoryEventDetailResponse(
            item.EventId,
            item.EventType,
            item.OccurredAt,
            item.RecordedAt,
            item.Source,
            ToResponse(new ClinicalHistoryProvenance(
                result.AuthoritativeSource.SourceType,
                result.AuthoritativeSource.Id,
                result.AuthoritativeSource.QuestionnaireVersionId,
                result.AuthoritativeSource.ClinicalRuleSetVersionId)),
            summary is null
                ? null
                : new ClinicalHistoryPrimarySymptomResponse(
                    summary.PrimarySymptom.Code,
                    summary.PrimarySymptom.Display),
            summary is null
                ? null
                : new ClinicalHistoryDurationResponse(
                    summary.Duration.Value,
                    summary.Duration.Unit),
            summary?.Intensity,
            summary?.AdditionalSymptoms,
            result.Amendments.Select(amendment =>
                ToResponse(amendment))
                .ToArray()));
    }

    private static async Task<IResult> AmendPreTriageEpisodeAsync(
        Guid episodeId,
        AmendPreTriageEpisodeRequest request,
        AmendPreTriageEpisode useCase,
        CancellationToken cancellationToken)
    {
        if (episodeId == Guid.Empty)
        {
            throw new PatientProfileNotFoundException();
        }

        var result = await useCase.ExecuteAsync(
            new AmendPreTriageEpisodeCommand(
                EntityId.From(episodeId),
                request.IdempotencyKey,
                request.Reason,
                request.AdditionalFields is { Count: > 0 }),
            cancellationToken);
        var amendment = result.Amendment;
        var response = new ClinicalHistoryAmendmentResponse(
            amendment.Id.Value,
            amendment.Reason.Value,
            new ClinicalHistoryAmendmentAuthorResponse(
                "BEEEXY_ACCOUNT",
                result.AuthorBeeexyId),
            amendment.CreatedAt,
            ToResponse(new ClinicalHistoryProvenance(
                amendment.SourceType,
                amendment.SourceId,
                amendment.SourceQuestionnaireVersionId,
                amendment.SourceClinicalRuleSetVersionId)));
        return Results.Created(
            $"/api/v1/pre-triage/episodes/{episodeId:D}/amendments/{amendment.Id.Value:D}",
            response);
    }

    private static ClinicalHistoryItemResponse ToResponse(
        ClinicalHistoryListItem item) =>
        new(
            item.EventId.Value,
            ClinicalHistoryEventTypes.ToApiValue(item.EventType),
            item.OccurredAt,
            item.RecordedAt,
            new ClinicalHistorySourceResponse(
                ToApiValue(item.SourceType),
                item.SourceId.Value,
                item.QuestionnaireVersionId.Value,
                item.ClinicalRuleSetVersionId.Value));

    private static string ToApiValue(AuthoritativeClinicalSourceType sourceType) =>
        sourceType switch
        {
            AuthoritativeClinicalSourceType.PreTriageEpisode =>
                "PRE_TRIAGE_EPISODE",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceType))
        };

    private static ClinicalHistoryProvenanceResponse ToResponse(
        ClinicalHistoryProvenance provenance) =>
        new(
            ToApiValue(provenance.SourceType),
            provenance.SourceId.Value,
            provenance.QuestionnaireVersionId.Value,
            provenance.ClinicalRuleSetVersionId.Value);

    private static ClinicalHistoryAmendmentResponse ToResponse(
        ClinicalHistoryAmendmentDetail amendment) =>
        new(
            amendment.AmendmentId.Value,
            amendment.Reason,
            new ClinicalHistoryAmendmentAuthorResponse(
                "BEEEXY_ACCOUNT",
                amendment.Author.BeeexyId),
            amendment.CreatedAt,
            ToResponse(amendment.Provenance));
}

internal sealed record AmendPreTriageEpisodeRequest
{
    public string? IdempotencyKey { get; init; }

    public string? Reason { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record ClinicalHistoryPageResponse(
    IReadOnlyList<ClinicalHistoryItemResponse> Items,
    string? NextCursor);

internal sealed record ClinicalHistoryItemResponse(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    ClinicalHistorySourceResponse Source);

internal sealed record ClinicalHistorySourceResponse(
    string Type,
    Guid Id,
    Guid QuestionnaireVersionId,
    Guid ClinicalRuleSetVersionId);

internal sealed record ClinicalHistoryEventDetailResponse(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    ClinicalHistorySourceResponse Source,
    ClinicalHistoryProvenanceResponse Provenance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ClinicalHistoryPrimarySymptomResponse? PrimarySymptom,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ClinicalHistoryDurationResponse? Duration,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Intensity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? AdditionalSymptoms,
    IReadOnlyList<ClinicalHistoryAmendmentResponse> Amendments);

internal sealed record ClinicalHistoryPrimarySymptomResponse(string Code, string Display);

internal sealed record ClinicalHistoryDurationResponse(decimal Value, string Unit);

internal sealed record ClinicalHistoryProvenanceResponse(
    string SourceType,
    Guid SourceId,
    Guid QuestionnaireVersionId,
    Guid ClinicalRuleSetVersionId);

internal sealed record ClinicalHistoryAmendmentResponse(
    Guid AmendmentId,
    string Reason,
    ClinicalHistoryAmendmentAuthorResponse Author,
    DateTimeOffset CreatedAt,
    ClinicalHistoryProvenanceResponse Provenance);

internal sealed record ClinicalHistoryAmendmentAuthorResponse(
    string Type,
    string? BeeexyId);
