using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;

namespace Beeexy.Application.History;

public sealed class ListClinicalHistory(
    AuthorizePatientAccess authorizePatientAccess,
    IClinicalHistoryReadRepository repository)
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;

    public async Task<ListClinicalHistoryResult> ExecuteAsync(
        ListClinicalHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var authorization = await authorizePatientAccess.ExecuteAsync(
            query.PatientProfileId,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new PatientProfileNotFoundException();
        }

        var eventType = ClinicalHistoryEventTypes.ParseOptionalFilter(query.EventType);
        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new RequestValidationException(
                "clinical_history.page_size_invalid",
                $"Page size must be between 1 and {MaximumPageSize}.");
        }

        var cursor = query.Cursor is null
            ? null
            : ClinicalHistoryCursorCodec.Decode(
                query.Cursor,
                query.PatientProfileId,
                eventType);
        if (cursor is not null &&
            !await repository.CursorExistsAsync(cursor, cancellationToken))
        {
            throw ClinicalHistoryCursorCodec.CreateInvalidCursorException();
        }

        var page = await repository.ListAsync(
            query.PatientProfileId,
            eventType,
            cursor,
            pageSize + 1,
            cancellationToken);

        var hasMore = page.Count > pageSize;
        var items = page.Take(pageSize).ToArray();
        var nextCursor = hasMore
            ? ClinicalHistoryCursorCodec.Encode(new ClinicalHistoryPageCursor(
                query.PatientProfileId,
                eventType,
                items[^1].OccurredAt,
                items[^1].EventId))
            : null;

        return new ListClinicalHistoryResult(items, nextCursor);
    }
}

public sealed record ListClinicalHistoryQuery(
    EntityId PatientProfileId,
    string? Cursor = null,
    int? PageSize = null,
    string? EventType = null);

public sealed record ListClinicalHistoryResult(
    IReadOnlyList<ClinicalHistoryListItem> Items,
    string? NextCursor);

public static class ClinicalHistoryEventTypes
{
    public const string CompletedPreTriage = "COMPLETED_PRE_TRIAGE";

    public static ClinicalHistoryEventType? ParseOptionalFilter(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return ParseFilter(value);
    }

    internal static ClinicalHistoryEventType ParseFilter(string value)
    {
        if (string.Equals(value, CompletedPreTriage, StringComparison.Ordinal))
        {
            return ClinicalHistoryEventType.CompletedPreTriage;
        }

        throw new RequestValidationException(
            "clinical_history.event_type_invalid",
            "The clinical history event type is not supported.");
    }

    public static string ToApiValue(ClinicalHistoryEventType eventType) =>
        eventType switch
        {
            ClinicalHistoryEventType.CompletedPreTriage => CompletedPreTriage,
            _ => throw new ArgumentOutOfRangeException(nameof(eventType))
        };
}
