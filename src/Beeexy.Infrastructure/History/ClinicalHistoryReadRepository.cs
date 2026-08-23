using Beeexy.Application.History;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.History;

internal sealed class ClinicalHistoryReadRepository(BeeexyDbContext dbContext)
    : IClinicalHistoryReadRepository
{
    public async Task<bool> CursorExistsAsync(
        ClinicalHistoryPageCursor cursor,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ClinicalHistoryEvents
            .AsNoTracking()
            .Where(historyEvent =>
                historyEvent.PatientProfileId == cursor.PatientProfileId &&
                historyEvent.Id == cursor.EventId &&
                historyEvent.OccurredAt == cursor.OccurredAt);
        if (cursor.EventType is { } eventType)
        {
            query = query.Where(historyEvent => historyEvent.EventType == eventType);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClinicalHistoryListItem>> ListAsync(
        EntityId patientProfileId,
        ClinicalHistoryEventType? eventType,
        ClinicalHistoryPageCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        var events = BuildQuery(patientProfileId, eventType, after, take)
            .AsNoTracking();
        var page = await events.ToListAsync(cancellationToken);

        return page.Select(historyEvent => new ClinicalHistoryListItem(
            historyEvent.Id,
            historyEvent.EventType,
            historyEvent.OccurredAt,
            historyEvent.RecordedAt,
            historyEvent.SourceType,
            historyEvent.SourceId,
            historyEvent.SourceQuestionnaireVersionId,
            historyEvent.SourceClinicalRuleSetVersionId)).ToArray();
    }

    private IQueryable<ClinicalHistoryEvent> BuildQuery(
        EntityId patientProfileId,
        ClinicalHistoryEventType? eventType,
        ClinicalHistoryPageCursor? after,
        int take)
    {
        if (after is null && eventType is null)
        {
            return dbContext.ClinicalHistoryEvents.FromSqlInterpolated($"""
                SELECT history_event.*
                FROM history.clinical_history_events AS history_event
                WHERE history_event.patient_profile_id = {patientProfileId.Value}
                ORDER BY history_event.occurred_at DESC, history_event.id DESC
                LIMIT {take}
                """);
        }

        if (after is null)
        {
            var storedEventType = ClinicalHistoryPersistence.StoreEventType(eventType!.Value);
            return dbContext.ClinicalHistoryEvents.FromSqlInterpolated($"""
                SELECT history_event.*
                FROM history.clinical_history_events AS history_event
                WHERE history_event.patient_profile_id = {patientProfileId.Value}
                  AND history_event.event_type = {storedEventType}
                ORDER BY history_event.occurred_at DESC, history_event.id DESC
                LIMIT {take}
                """);
        }

        if (eventType is null)
        {
            return dbContext.ClinicalHistoryEvents.FromSqlInterpolated($"""
                SELECT history_event.*
                FROM history.clinical_history_events AS history_event
                WHERE history_event.patient_profile_id = {patientProfileId.Value}
                  AND (
                    history_event.occurred_at < {after.OccurredAt}
                    OR (
                      history_event.occurred_at = {after.OccurredAt}
                      AND history_event.id < {after.EventId.Value}
                    )
                  )
                ORDER BY history_event.occurred_at DESC, history_event.id DESC
                LIMIT {take}
                """);
        }

        var filteredEventType = ClinicalHistoryPersistence.StoreEventType(eventType.Value);
        return dbContext.ClinicalHistoryEvents.FromSqlInterpolated($"""
            SELECT history_event.*
            FROM history.clinical_history_events AS history_event
            WHERE history_event.patient_profile_id = {patientProfileId.Value}
              AND history_event.event_type = {filteredEventType}
              AND (
                history_event.occurred_at < {after.OccurredAt}
                OR (
                  history_event.occurred_at = {after.OccurredAt}
                  AND history_event.id < {after.EventId.Value}
                )
              )
            ORDER BY history_event.occurred_at DESC, history_event.id DESC
            LIMIT {take}
            """);
    }
}
