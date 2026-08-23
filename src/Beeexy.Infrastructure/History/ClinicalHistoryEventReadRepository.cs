using Beeexy.Application.History;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.History;

internal sealed class ClinicalHistoryEventReadRepository(BeeexyDbContext dbContext)
    : IClinicalHistoryEventReadRepository
{
    public async Task<ClinicalHistoryEventDetail?> GetAsync(
        EntityId patientProfileId,
        EntityId eventId,
        CancellationToken cancellationToken = default)
    {
        var historyEvent = await dbContext.ClinicalHistoryEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.PatientProfileId == patientProfileId &&
                    candidate.Id == eventId,
                cancellationToken);
        if (historyEvent is null)
        {
            return null;
        }

        var source = await dbContext.PreTriageEpisodes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                episode =>
                    episode.Id == historyEvent.SourceId &&
                    episode.PatientProfileId == patientProfileId &&
                    episode.QuestionnaireVersionId ==
                        historyEvent.SourceQuestionnaireVersionId &&
                    episode.ClinicalRuleSetVersionId ==
                        historyEvent.SourceClinicalRuleSetVersionId &&
                    episode.CompletedAt == historyEvent.OccurredAt,
                cancellationToken);
        if (source is null)
        {
            return null;
        }

        var amendments = await (
                from amendment in dbContext.ClinicalAmendments.AsNoTracking()
                where amendment.ClinicalHistoryEventId == historyEvent.Id &&
                    amendment.SourceType == historyEvent.SourceType &&
                    amendment.SourceId == historyEvent.SourceId &&
                    amendment.SourceQuestionnaireVersionId ==
                        historyEvent.SourceQuestionnaireVersionId &&
                    amendment.SourceClinicalRuleSetVersionId ==
                        historyEvent.SourceClinicalRuleSetVersionId
                join authorProfile in dbContext.PatientProfiles.AsNoTracking()
                    on (EntityId?)amendment.AuthorAccountId equals
                        authorProfile.AccountId into authorProfiles
                from authorProfile in authorProfiles.DefaultIfEmpty()
                orderby amendment.CreatedAt, amendment.Id
                select new
                {
                    Amendment = amendment,
                    AuthorProfile = authorProfile
                })
            .ToArrayAsync(cancellationToken);

        var listItem = new ClinicalHistoryListItem(
            historyEvent.Id,
            historyEvent.EventType,
            historyEvent.OccurredAt,
            historyEvent.RecordedAt,
            historyEvent.SourceType,
            historyEvent.SourceId,
            historyEvent.SourceQuestionnaireVersionId,
            historyEvent.SourceClinicalRuleSetVersionId);
        var sourceDetail = new ClinicalHistorySourceDetail(
            historyEvent.SourceType,
            source.Id,
            source.CompletedAt,
            source.QuestionnaireVersionId,
            source.ClinicalRuleSetVersionId);
        var amendmentDetails = amendments.Select(row =>
            new ClinicalHistoryAmendmentDetail(
                row.Amendment.Id,
                row.Amendment.Reason.Value,
                new ClinicalHistoryAmendmentAuthor(
                    row.AuthorProfile?.BeeexyId.Value),
                row.Amendment.CreatedAt,
                new ClinicalHistoryProvenance(
                    row.Amendment.SourceType,
                    row.Amendment.SourceId,
                    row.Amendment.SourceQuestionnaireVersionId,
                    row.Amendment.SourceClinicalRuleSetVersionId)))
            .ToArray();

        return new ClinicalHistoryEventDetail(
            listItem,
            sourceDetail,
            amendmentDetails);
    }
}
