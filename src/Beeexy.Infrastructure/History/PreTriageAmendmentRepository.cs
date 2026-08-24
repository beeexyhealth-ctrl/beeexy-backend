using Beeexy.Application.History;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Infrastructure.History;

internal sealed class PreTriageAmendmentRepository(BeeexyDbContext dbContext)
    : IPreTriageAmendmentRepository
{
    private const string DuplicateConstraint =
        "ux_clinical_amendments_event_idempotency_key";

    public async Task<ClinicalAmendment?> CreateLockedAsync(
        EntityId episodeId,
        Func<AmendablePreTriageSource, Task<ClinicalAmendment>> createAmendment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createAmendment);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        var episodes = await dbContext.PreTriageEpisodes
            .FromSqlInterpolated($"""
                SELECT * FROM triage.pre_triage_episodes
                WHERE id = {episodeId.Value}
                FOR SHARE
                """)
            .ToArrayAsync(cancellationToken);
        var episode = episodes.SingleOrDefault();
        if (episode?.PatientProfileId is not { } patientProfileId)
        {
            return null;
        }

        var historyEvents = await dbContext.ClinicalHistoryEvents
            .FromSqlInterpolated($"""
                SELECT * FROM history.clinical_history_events
                WHERE source_type = 'pre_triage_episode'
                AND source_id = {episodeId.Value}
                FOR SHARE
                """)
            .ToArrayAsync(cancellationToken);
        var historyEvent = historyEvents.SingleOrDefault();
        if (historyEvent is null ||
            historyEvent.EventType != ClinicalHistoryEventType.CompletedPreTriage ||
            historyEvent.PatientProfileId != patientProfileId ||
            historyEvent.SourceQuestionnaireVersionId != episode.QuestionnaireVersionId ||
            historyEvent.SourceClinicalRuleSetVersionId !=
                episode.ClinicalRuleSetVersionId ||
            historyEvent.OccurredAt != episode.CompletedAt)
        {
            return null;
        }

        var amendment = await createAmendment(
            new AmendablePreTriageSource(patientProfileId, historyEvent));
        EnsureMatchesSource(amendment, historyEvent);
        dbContext.ClinicalAmendments.Add(amendment);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return amendment;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: DuplicateConstraint
            })
        {
            throw new ClinicalAmendmentDuplicateException();
        }
    }

    private static void EnsureMatchesSource(
        ClinicalAmendment amendment,
        ClinicalHistoryEvent historyEvent)
    {
        ArgumentNullException.ThrowIfNull(amendment);
        if (amendment.ClinicalHistoryEventId != historyEvent.Id ||
            amendment.SourceReference != historyEvent.SourceReference ||
            amendment.SourceProvenance != historyEvent.SourceProvenance ||
            amendment.IdempotencyKey is null)
        {
            throw new InvalidOperationException(
                "The amendment mutation does not match its locked history source.");
        }
    }
}
