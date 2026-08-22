using Beeexy.Domain.Common;

namespace Beeexy.Domain.Triage;

public sealed class ClinicalAssessment
{
    private readonly List<ClinicalFinding> _findings = [];

    private ClinicalAssessment()
    {
    }

    private ClinicalAssessment(
        EntityId id,
        EntityId episodeId,
        EntityId clinicalRuleSetVersionId,
        UrgencyCode? urgencyCode,
        string? resultMessageReference,
        DateTimeOffset createdAt)
    {
        Id = id;
        EpisodeId = episodeId;
        ClinicalRuleSetVersionId = clinicalRuleSetVersionId;
        UrgencyCode = urgencyCode;
        ResultMessageReference = resultMessageReference;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId EpisodeId { get; private set; }

    public EntityId ClinicalRuleSetVersionId { get; private set; }

    public UrgencyCode? UrgencyCode { get; private set; }

    public string? ResultMessageReference { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<ClinicalFinding> Findings => _findings.AsReadOnly();

    public static ClinicalAssessment Create(
        PreTriageEpisode episode,
        UrgencyCode urgencyCode,
        DateTimeOffset createdAt,
        IEnumerable<ClinicalFindingInput>? findings = null,
        string? resultMessageReference = null,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(urgencyCode);
        InstantGuard.EnsureNotBefore(createdAt, episode.CompletedAt, nameof(createdAt));

        var assessment = new ClinicalAssessment(
            id ?? EntityId.New(),
            episode.Id,
            episode.ClinicalRuleSetVersionId,
            urgencyCode,
            TriageValueGuard.OptionalText(
                resultMessageReference,
                TriagePersistenceLimits.MaximumReferenceLength,
                nameof(resultMessageReference)),
            createdAt);

        if (findings is null)
        {
            return assessment;
        }

        foreach (var finding in findings)
        {
            ArgumentNullException.ThrowIfNull(finding);
            assessment._findings.Add(ClinicalFinding.Create(
                assessment.Id,
                finding.FindingCode,
                finding.SourceRuleCode,
                finding.MessageReference,
                createdAt));
        }

        return assessment;
    }

    public static ClinicalAssessment CreateNeutral(
        PreTriageEpisode episode,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(episode);
        InstantGuard.EnsureNotBefore(createdAt, episode.CompletedAt, nameof(createdAt));

        return new ClinicalAssessment(
            id ?? EntityId.New(),
            episode.Id,
            episode.ClinicalRuleSetVersionId,
            urgencyCode: null,
            resultMessageReference: null,
            createdAt);
    }
}
