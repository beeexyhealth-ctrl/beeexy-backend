using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageIntakeReplayRepository(BeeexyDbContext dbContext)
    : IPreTriageIntakeReplayRepository
{
    public async Task<PreTriageIntakeReplayState?> LoadAsync(
        EntityId sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.PreTriageSessions
            .AsNoTracking()
            .Include(value => value.Answers)
            .SingleOrDefaultAsync(value => value.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        if (session.Answers.Count > 0 || session.Status ==
            Domain.Triage.PreTriageSessionStatus.Active)
        {
            return new PreTriageIntakeReplayState(session, session.Answers);
        }

        var episode = await dbContext.PreTriageEpisodes
            .AsNoTracking()
            .Include(value => value.Answers)
            .SingleOrDefaultAsync(
                value => value.SourceSessionId == sessionId,
                cancellationToken);
        return new PreTriageIntakeReplayState(
            session,
            episode?.Answers ?? []);
    }
}
