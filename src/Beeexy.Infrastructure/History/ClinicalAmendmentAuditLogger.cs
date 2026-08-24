using Beeexy.Application.History;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.History;

internal sealed class ClinicalAmendmentAuditLogger(
    ILogger<ClinicalAmendmentAuditLogger> logger) : IClinicalAmendmentAuditLogger
{
    public void Created(
        EntityId actorAccountId,
        EntityId historyEventId,
        EntityId sourceEpisodeId,
        EntityId amendmentId,
        PatientAccessReason accessReason,
        DateTimeOffset createdAt) =>
        logger.LogInformation(
            "Clinical amendment {AmendmentId} created by account {ActorAccountId} " +
            "for event {HistoryEventId}, source {SourceEpisodeId}, via {AccessReason} at {CreatedAt}.",
            amendmentId.Value,
            actorAccountId.Value,
            historyEventId.Value,
            sourceEpisodeId.Value,
            accessReason,
            createdAt);

    public void DuplicateRejected(
        EntityId actorAccountId,
        EntityId sourceEpisodeId,
        PatientAccessReason? accessReason,
        DateTimeOffset rejectedAt) =>
        logger.LogWarning(
            "Duplicate clinical amendment rejected for source {SourceEpisodeId} by " +
            "account {ActorAccountId}, via {AccessReason} at {RejectedAt}.",
            sourceEpisodeId.Value,
            actorAccountId.Value,
            accessReason,
            rejectedAt);
}
