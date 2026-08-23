using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class ProjectCompletedPreTriageEpisode(
    IClock clock,
    IPreTriageHistoryProjectionRepository repository) : IPreTriageHistoryProjector
{
    public Task<PreTriageHistoryProjectionOutcome?> ExecuteAsync(
        EntityId sourceEpisodeId,
        CancellationToken cancellationToken = default)
    {
        var recordedAt = ToPostgreSqlPrecision(clock.UtcNow);
        return repository.ProjectAsync(
            sourceEpisodeId,
            graph =>
            {
                EnsureEligible(graph);
                return ClinicalHistoryEvent.CreateCompletedPreTriage(
                    graph.Episode,
                    recordedAt);
            },
            cancellationToken);
    }

    private static void EnsureEligible(PreTriageHistoryProjectionGraph graph)
    {
        var session = graph.Session;
        var episode = graph.Episode;
        var assessment = graph.Assessment;
        var record = graph.Record;
        var claimedAnonymous = session.IsAnonymous && episode.IsClaimed;
        var authenticatedCompletion = !session.IsAnonymous && !episode.IsClaimed;

        if (session.Status != PreTriageSessionStatus.Completed ||
            session.CompletedAt != episode.CompletedAt ||
            episode.SourceSessionId != session.Id ||
            episode.QuestionnaireVersionId != session.QuestionnaireVersionId ||
            episode.PatientProfileId is null ||
            (!claimedAnonymous && !authenticatedCompletion) ||
            (claimedAnonymous &&
                (episode.ClaimedAt is null ||
                 episode.AnonymousExpiresAt != session.ExpiresAt)) ||
            (authenticatedCompletion &&
                (episode.PatientProfileId != session.PatientProfileId ||
                 episode.ClaimedAt is not null ||
                 episode.AnonymousExpiresAt is not null)) ||
            assessment.EpisodeId != episode.Id ||
            assessment.ClinicalRuleSetVersionId != episode.ClinicalRuleSetVersionId ||
            assessment.UrgencyCode is not null ||
            assessment.ResultMessageReference is not null ||
            assessment.Findings.Count != 0 ||
            record.SourceEpisodeId != episode.Id ||
            record.PatientProfileId != episode.PatientProfileId ||
            record.CompletedAt != episode.CompletedAt ||
            record.CreatedAt != (episode.ClaimedAt ?? episode.CompletedAt))
        {
            throw new InvalidOperationException(
                "The completed pre-triage projection graph is inconsistent.");
        }
    }

    private static DateTimeOffset ToPostgreSqlPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);
}

public sealed record PreTriageHistoryProjectionOutcome(
    ClinicalHistoryEvent Event,
    bool IsNewlyProjected);

public sealed record PreTriageHistoryProjectionGraph(
    PreTriageHistoryProjectionRecord Record,
    PreTriageSession Session,
    PreTriageEpisode Episode,
    ClinicalAssessment Assessment);

public interface IPreTriageHistoryProjectionRepository
{
    Task<PreTriageHistoryProjectionOutcome?> ProjectAsync(
        EntityId sourceEpisodeId,
        Func<PreTriageHistoryProjectionGraph, ClinicalHistoryEvent> createEvent,
        CancellationToken cancellationToken = default);
}

public interface IPreTriageHistoryProjector
{
    Task<PreTriageHistoryProjectionOutcome?> ExecuteAsync(
        EntityId sourceEpisodeId,
        CancellationToken cancellationToken = default);
}
