using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageCompletionRepository(BeeexyDbContext dbContext)
    : IPreTriageCompletionRepository
{
    public async Task<TResult?> ExecuteLockedAsync<TResult>(
        EntityId sessionId,
        Func<PreTriageSession, CompletedPreTriageGraph?,
            Task<PreTriageCompletionMutation<TResult>>> mutation,
        CancellationToken cancellationToken = default)
        where TResult : class
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

        await dbContext.Entry(session).Collection(value => value.Answers)
            .LoadAsync(cancellationToken);
        await dbContext.Entry(session).Collection(value => value.ReportedSymptoms)
            .LoadAsync(cancellationToken);

        var episode = await dbContext.PreTriageEpisodes
            .Include(value => value.Answers)
            .Include(value => value.ReportedSymptoms)
            .AsSplitQuery()
            .SingleOrDefaultAsync(value => value.SourceSessionId == sessionId,
                cancellationToken);
        CompletedPreTriageGraph? completed = null;
        if (episode is not null)
        {
            var assessment = await dbContext.ClinicalAssessments
                .Include(value => value.Findings)
                .SingleOrDefaultAsync(value => value.EpisodeId == episode.Id,
                    cancellationToken) ?? throw new InvalidOperationException(
                        "A completed pre-triage episode is missing its assessment.");
            completed = new CompletedPreTriageGraph(episode, assessment);
        }

        var decision = await mutation(session, completed);
        if (decision.NewEpisode is not null || decision.NewAssessment is not null)
        {
            if (decision.NewEpisode is null || decision.NewAssessment is null ||
                completed is not null)
            {
                throw new InvalidOperationException(
                    "The pre-triage completion mutation is inconsistent.");
            }

            dbContext.PreTriageEpisodes.Add(decision.NewEpisode);
            dbContext.ClinicalAssessments.Add(decision.NewAssessment);
            if (decision.NewEpisode.PatientProfileId.HasValue)
            {
                dbContext.PreTriageHistoryProjectionRecords.Add(
                    PreTriageHistoryProjectionRecord.Create(
                        decision.NewEpisode,
                        decision.NewEpisode.CompletedAt));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return decision.Result;
    }

    public async Task<StoredPreTriageGraph?> GetAsync(
        EntityId sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.PreTriageSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var episode = await dbContext.PreTriageEpisodes
            .AsNoTracking()
            .Include(value => value.Answers)
            .Include(value => value.ReportedSymptoms)
            .AsSplitQuery()
            .SingleOrDefaultAsync(value => value.SourceSessionId == sessionId,
                cancellationToken);
        ClinicalAssessment? assessment = null;
        if (episode is not null)
        {
            assessment = await dbContext.ClinicalAssessments
                .AsNoTracking()
                .Include(value => value.Findings)
                .SingleOrDefaultAsync(value => value.EpisodeId == episode.Id,
                    cancellationToken);
        }

        return new StoredPreTriageGraph(session, episode, assessment);
    }
}
