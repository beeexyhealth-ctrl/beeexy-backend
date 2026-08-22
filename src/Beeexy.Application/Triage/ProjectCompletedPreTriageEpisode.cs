using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class ProjectCompletedPreTriageEpisode(
    IClinicalDefinitionProvider definitionProvider,
    CheckDemoQuestionnaireCompleteness completeness,
    IPreTriageHistoryProjectionRepository repository) : IPreTriageHistoryProjector
{
    public async Task<PreTriageHistoryProjection?> ExecuteAsync(
        EntityId sourceEpisodeId,
        CancellationToken cancellationToken = default)
    {
        var graph = await repository.GetAsync(sourceEpisodeId, cancellationToken);
        if (graph is null)
        {
            return null;
        }

        EnsureEligible(graph);
        var package = await definitionProvider.GetDefinitionByQuestionnaireIdAsync(
            graph.Episode.QuestionnaireVersionId,
            cancellationToken) ?? throw new InvalidOperationException(
                "The projection source's frozen questionnaire package is unavailable.");
        if (package.Profile != ClinicalDefinitionPackageProfile.SimplifiedDemoIntake ||
            package.Questionnaire.Id != graph.Episode.QuestionnaireVersionId ||
            package.RuleSet.Id != graph.Episode.ClinicalRuleSetVersionId ||
            package.ContentStatus != ClinicalContentStatus.NonClinicalDemo)
        {
            throw new InvalidOperationException(
                "The projection source's frozen definition provenance is inconsistent.");
        }

        var summary = completeness.CheckPermanent(graph.Episode, package);
        return new PreTriageHistoryProjection(
            SourceType: PreTriageHistoryProjection.SourceTypeCode,
            graph.Episode.Id,
            graph.Episode.PatientProfileId!.Value,
            graph.Episode.CompletedAt,
            package.Pathway,
            summary.PrimarySymptomDisplay,
            summary.DurationValue,
            summary.DurationUnit,
            summary.Intensity,
            summary.AdditionalSymptoms,
            package.Questionnaire.QuestionnaireCode,
            package.Questionnaire.Version,
            package.RuleSet.RuleSetCode,
            package.RuleSet.Version,
            package.ContentStatus);
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
}

public sealed record PreTriageHistoryProjection(
    string SourceType,
    EntityId SourceEpisodeId,
    EntityId PatientProfileId,
    DateTimeOffset CompletedAt,
    ClinicalPathwayCode PrimarySymptom,
    string PrimarySymptomDisplay,
    decimal DurationValue,
    string DurationUnit,
    int Intensity,
    IReadOnlyList<string> AdditionalSymptoms,
    QuestionnaireCode QuestionnaireCode,
    DefinitionVersion QuestionnaireVersion,
    RuleSetCode PackageCode,
    DefinitionVersion PackageVersion,
    ClinicalContentStatus ContentStatus)
{
    public const string SourceTypeCode = "PRE_TRIAGE_EPISODE";
}

public sealed record PreTriageHistoryProjectionGraph(
    PreTriageHistoryProjectionRecord Record,
    PreTriageSession Session,
    PreTriageEpisode Episode,
    ClinicalAssessment Assessment);

public interface IPreTriageHistoryProjectionRepository
{
    Task<PreTriageHistoryProjectionGraph?> GetAsync(
        EntityId sourceEpisodeId,
        CancellationToken cancellationToken = default);
}

public interface IPreTriageHistoryProjector
{
    Task<PreTriageHistoryProjection?> ExecuteAsync(
        EntityId sourceEpisodeId,
        CancellationToken cancellationToken = default);
}
