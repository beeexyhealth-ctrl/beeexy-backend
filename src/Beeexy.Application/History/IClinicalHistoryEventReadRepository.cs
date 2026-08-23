using Beeexy.Domain.Common;
using Beeexy.Domain.History;

namespace Beeexy.Application.History;

public interface IClinicalHistoryEventReadRepository
{
    Task<ClinicalHistoryEventDetail?> GetAsync(
        EntityId patientProfileId,
        EntityId eventId,
        CancellationToken cancellationToken = default);
}

public sealed record ClinicalHistoryEventDetail(
    ClinicalHistoryListItem Event,
    ClinicalHistorySourceDetail AuthoritativeSource,
    IReadOnlyList<ClinicalHistoryAmendmentDetail> Amendments);

public sealed record ClinicalHistorySourceDetail(
    AuthoritativeClinicalSourceType SourceType,
    EntityId Id,
    DateTimeOffset CompletedAt,
    EntityId QuestionnaireVersionId,
    EntityId ClinicalRuleSetVersionId);

public sealed record ClinicalHistoryAmendmentDetail(
    EntityId AmendmentId,
    string Reason,
    ClinicalHistoryAmendmentAuthor Author,
    DateTimeOffset CreatedAt,
    ClinicalHistoryProvenance Provenance);

public sealed record ClinicalHistoryAmendmentAuthor(string? BeeexyId);

public sealed record ClinicalHistoryProvenance(
    AuthoritativeClinicalSourceType SourceType,
    EntityId SourceId,
    EntityId QuestionnaireVersionId,
    EntityId ClinicalRuleSetVersionId);
