using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageHistoryProjectionRepository(BeeexyDbContext dbContext)
    : IPreTriageHistoryProjectionRepository
{
    public async Task<PreTriageHistoryProjectionGraph?> GetAsync(
        EntityId sourceEpisodeId,
        CancellationToken cancellationToken = default)
    {
        var record = await dbContext.PreTriageHistoryProjectionRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.SourceEpisodeId == sourceEpisodeId,
                cancellationToken);
        if (record is null)
        {
            return null;
        }

        var episode = await dbContext.PreTriageEpisodes
            .AsNoTracking()
            .Include(value => value.Answers)
            .Include(value => value.ReportedSymptoms)
            .AsSplitQuery()
            .SingleAsync(value => value.Id == sourceEpisodeId, cancellationToken);
        var session = await dbContext.PreTriageSessions
            .AsNoTracking()
            .SingleAsync(value => value.Id == episode.SourceSessionId, cancellationToken);
        var assessment = await dbContext.ClinicalAssessments
            .AsNoTracking()
            .Include(value => value.Findings)
            .SingleAsync(value => value.EpisodeId == episode.Id, cancellationToken);

        return new PreTriageHistoryProjectionGraph(record, session, episode, assessment);
    }
}
