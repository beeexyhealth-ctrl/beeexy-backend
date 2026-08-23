using Beeexy.Domain.Common;
using Beeexy.Domain.History;

namespace Beeexy.Application.History;

public interface IClinicalHistoryReadRepository
{
    Task<bool> CursorExistsAsync(
        ClinicalHistoryPageCursor cursor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClinicalHistoryListItem>> ListAsync(
        EntityId patientProfileId,
        ClinicalHistoryEventType? eventType,
        ClinicalHistoryPageCursor? after,
        int take,
        CancellationToken cancellationToken = default);
}

public sealed record ClinicalHistoryListItem(
    EntityId EventId,
    ClinicalHistoryEventType EventType,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    AuthoritativeClinicalSourceType SourceType,
    EntityId SourceId,
    EntityId QuestionnaireVersionId,
    EntityId ClinicalRuleSetVersionId);

public sealed record ClinicalHistoryPageCursor(
    EntityId PatientProfileId,
    ClinicalHistoryEventType? EventType,
    DateTimeOffset OccurredAt,
    EntityId EventId);
