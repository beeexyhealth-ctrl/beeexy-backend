using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageIntakeAuditLogger(
    ILogger<PreTriageIntakeAuditLogger> logger) : IPreTriageIntakeAuditLogger
{
    public void InterpretationEvaluated(
        EntityId sessionId,
        bool usedNaturalLanguage,
        TriageIntakeSubmissionOutcome outcome,
        int acceptedCandidateCategoryCount)
    {
        logger.LogInformation(
            "Pre-triage intake evaluated for session {SessionId}; natural language {UsedNaturalLanguage}, outcome {Outcome}, accepted candidate categories {AcceptedCategoryCount}.",
            sessionId.Value,
            usedNaturalLanguage,
            outcome,
            acceptedCandidateCategoryCount);
    }

    public void AnswersProcessed(
        EntityId sessionId,
        TriageIntakeSubmissionOutcome outcome,
        int acceptedAnswerCategoryCount,
        bool readyToComplete)
    {
        logger.LogInformation(
            "Pre-triage answers processed for session {SessionId}; outcome {Outcome}, accepted answer categories {AcceptedCategoryCount}, ready {ReadyToComplete}.",
            sessionId.Value,
            outcome,
            acceptedAnswerCategoryCount,
            readyToComplete);
    }
}
