using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageCompletionAuditLogger(
    ILogger<PreTriageCompletionAuditLogger> logger) : IPreTriageCompletionAuditLogger
{
    public void CompletionProcessed(
        EntityId sessionId,
        PreTriageCallerMode callerMode,
        bool newlyCompleted,
        DateTimeOffset completedAt) =>
        logger.LogInformation(
            "Pre-triage completion processed for session {SessionId}; access {AccessType}; " +
            "new completion {NewlyCompleted}; completed at {CompletedAt}.",
            sessionId.Value,
            callerMode,
            newlyCompleted,
            completedAt);

    public void ResultRetrieved(
        EntityId sessionId,
        PreTriageCallerMode callerMode,
        DateTimeOffset completedAt) =>
        logger.LogInformation(
            "Pre-triage result retrieved for session {SessionId}; access {AccessType}; " +
            "completed at {CompletedAt}.",
            sessionId.Value,
            callerMode,
            completedAt);
}
