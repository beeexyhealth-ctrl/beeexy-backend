using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Domain.History;

public sealed record ClinicalSourceProvenance
{
    private ClinicalSourceProvenance(
        EntityId questionnaireVersionId,
        EntityId clinicalRuleSetVersionId)
    {
        QuestionnaireVersionId = questionnaireVersionId;
        ClinicalRuleSetVersionId = clinicalRuleSetVersionId;
    }

    public EntityId QuestionnaireVersionId { get; }

    public EntityId ClinicalRuleSetVersionId { get; }

    public static ClinicalSourceProvenance FromCompletedPreTriage(
        PreTriageEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return new ClinicalSourceProvenance(
            episode.QuestionnaireVersionId,
            episode.ClinicalRuleSetVersionId);
    }

    internal static ClinicalSourceProvenance FromCompletedPreTriageSource(
        EntityId questionnaireVersionId,
        EntityId clinicalRuleSetVersionId)
    {
        EnsureNonEmpty(questionnaireVersionId, nameof(questionnaireVersionId));
        EnsureNonEmpty(clinicalRuleSetVersionId, nameof(clinicalRuleSetVersionId));
        return new ClinicalSourceProvenance(
            questionnaireVersionId,
            clinicalRuleSetVersionId);
    }

    private static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }
}
