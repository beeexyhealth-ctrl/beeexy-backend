using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageHistoryProjectionRepository(BeeexyDbContext dbContext)
    : IPreTriageHistoryProjectionRepository
{
    public async Task<PreTriageHistoryProjectionOutcome?> ProjectAsync(
        EntityId sourceEpisodeId,
        Func<PreTriageHistoryProjectionGraph, ClinicalHistoryEvent> createEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createEvent);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);

        var existing = await LoadExistingAsync(sourceEpisodeId, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new PreTriageHistoryProjectionOutcome(
                existing,
                IsNewlyProjected: false);
        }

        var record = await dbContext.PreTriageHistoryProjectionRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.SourceEpisodeId == sourceEpisodeId,
                cancellationToken);
        if (record is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var episode = await dbContext.PreTriageEpisodes
            .AsNoTracking()
            .SingleAsync(value => value.Id == sourceEpisodeId, cancellationToken);
        var session = await dbContext.PreTriageSessions
            .AsNoTracking()
            .SingleAsync(value => value.Id == episode.SourceSessionId, cancellationToken);
        var assessment = await dbContext.ClinicalAssessments
            .AsNoTracking()
            .Include(value => value.Findings)
            .SingleAsync(value => value.EpisodeId == episode.Id, cancellationToken);
        var historyEvent = createEvent(new PreTriageHistoryProjectionGraph(
            record,
            session,
            episode,
            assessment));

        dbContext.ClinicalHistoryEvents.Add(historyEvent);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PreTriageHistoryProjectionOutcome(
                historyEvent,
                IsNewlyProjected: true);
        }
        catch (DbUpdateException exception) when (IsDuplicateProjection(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var winner = await LoadExistingAsync(sourceEpisodeId, cancellationToken) ??
                throw new InvalidOperationException(
                    "The concurrent Clinical History projection winner is unavailable.",
                    exception);
            return new PreTriageHistoryProjectionOutcome(
                winner,
                IsNewlyProjected: false);
        }
    }

    private Task<ClinicalHistoryEvent?> LoadExistingAsync(
        EntityId sourceEpisodeId,
        CancellationToken cancellationToken) =>
        dbContext.ClinicalHistoryEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(historyEvent =>
                historyEvent.SourceId == sourceEpisodeId &&
                historyEvent.SourceType ==
                    AuthoritativeClinicalSourceType.PreTriageEpisode &&
                historyEvent.EventType ==
                    ClinicalHistoryEventType.CompletedPreTriage,
                cancellationToken);

    private static bool IsDuplicateProjection(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_clinical_history_events_source_projection"
        };
}
