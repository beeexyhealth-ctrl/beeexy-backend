using Beeexy.Application.History;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;

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

        endpoints.MapGet(
                "/api/v1/patients/{patientId:guid}/clinical-history/{eventId:guid}",
                GetClinicalHistoryEventAsync)
            .WithName("GetClinicalHistoryEvent")
            .WithTags("Clinical History")
            .WithDescription(
                "Returns one Clinical History event for an authorized primary or actively " +
                "managed patient, including its frozen authoritative Pre-Triage source " +
                "provenance and existing amendments in oldest-to-newest order. The original " +
                "source remains immutable. Absent patients, inaccessible patients, absent " +
                "events, and events belonging to another patient all return a concealed 404.")
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
            result.Amendments.Select(amendment =>
                new ClinicalHistoryAmendmentResponse(
                    amendment.AmendmentId.Value,
                    amendment.Reason,
                    new ClinicalHistoryAmendmentAuthorResponse(
                        "BEEEXY_ACCOUNT",
                        amendment.Author.BeeexyId),
                    amendment.CreatedAt,
                    ToResponse(amendment.Provenance)))
                .ToArray()));
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
    IReadOnlyList<ClinicalHistoryAmendmentResponse> Amendments);

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
