using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageInterpretationAuditLogger(
    ILogger<PreTriageInterpretationAuditLogger> logger)
    : IPreTriageInterpretationAuditLogger
{
    public void InterpretationEvaluated(
        PreTriageIntakeResolution resolution,
        ClinicalPathwayCode? pathway,
        bool usedAi,
        int acceptedCandidateCategoryCount)
    {
        logger.LogInformation(
            "Pre-session pre-triage interpretation evaluated; resolution {Resolution}, pathway {Pathway}, AI used {UsedAi}, accepted candidate categories {AcceptedCategoryCount}.",
            resolution,
            pathway?.Value,
            usedAi,
            acceptedCandidateCategoryCount);
    }

    public void InterpretationFailed(ClinicalAiProviderFailureCategory failure)
    {
        logger.LogWarning(
            "Pre-session pre-triage interpretation failed with safe provider category {FailureCategory}.",
            failure);
    }
}
