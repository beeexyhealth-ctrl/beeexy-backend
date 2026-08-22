using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageClaimRepository(BeeexyDbContext dbContext)
    : IPreTriageClaimRepository
{
    public async Task<ClaimAnonymousPreTriageMutation?> ExecuteLockedAsync(
        EntityId sessionId,
        Func<ClaimablePreTriageGraph, ClaimAnonymousPreTriageMutation> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        var session = await dbContext.PreTriageSessions
            .FromSqlInterpolated(
                $"SELECT * FROM triage.pre_triage_sessions WHERE id = {sessionId.Value} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return null;
        }

        var episode = await dbContext.PreTriageEpisodes
            .Include(value => value.Answers)
            .Include(value => value.ReportedSymptoms)
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                value => value.SourceSessionId == sessionId,
                cancellationToken);
        var assessment = episode is null
            ? null
            : await dbContext.ClinicalAssessments
                .Include(value => value.Findings)
                .SingleOrDefaultAsync(
                    value => value.EpisodeId == episode.Id,
                    cancellationToken);

        var decision = mutation(new ClaimablePreTriageGraph(
            session,
            episode,
            assessment));
        if (decision.IsNewlyClaimed)
        {
            if (episode is null || episode.PatientProfileId is null ||
                episode.ClaimedAt is null)
            {
                throw new InvalidOperationException(
                    "The pre-triage claim mutation is inconsistent.");
            }

            dbContext.PreTriageHistoryProjectionRecords.Add(
                PreTriageHistoryProjectionRecord.Create(
                    episode,
                    episode.ClaimedAt.Value));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return decision;
    }
}
