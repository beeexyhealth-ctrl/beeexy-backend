using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageSessionAuditLogger(
    ILogger<PreTriageSessionAuditLogger> logger) : IPreTriageSessionAuditLogger
{
    public void SessionCreated(
        EntityId sessionId,
        PreTriageCallerMode callerMode,
        ClinicalPathwayCode pathway,
        QuestionnaireCode questionnaireCode,
        DefinitionVersion questionnaireVersion,
        RuleSetCode ruleSetCode,
        DefinitionVersion ruleSetVersion,
        EntityId? patientProfileId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        logger.LogInformation(
            "Pre-triage session {SessionId} created in {CallerMode} mode for pathway {Pathway}, " +
            "questionnaire {QuestionnaireCode}/{QuestionnaireVersion}, rule set " +
            "{RuleSetCode}/{RuleSetVersion}, patient {PatientProfileId}, created {CreatedAt}, " +
            "expires {ExpiresAt}.",
            sessionId.Value,
            callerMode,
            pathway.Value,
            questionnaireCode.Value,
            questionnaireVersion.Value,
            ruleSetCode.Value,
            ruleSetVersion.Value,
            patientProfileId?.Value,
            createdAt,
            expiresAt);
    }

    public void SessionRejected(
        PreTriageCallerMode callerMode,
        string? pathway,
        PreTriageStartRejectionCategory category)
    {
        logger.LogInformation(
            "Pre-triage session start rejected in {CallerMode} mode for pathway {Pathway} " +
            "with category {RejectionCategory}.",
            callerMode,
            pathway,
            category);
    }
}
