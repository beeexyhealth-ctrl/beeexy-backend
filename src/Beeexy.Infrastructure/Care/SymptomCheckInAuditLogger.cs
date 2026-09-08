using Beeexy.Application.Care;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Care;

internal sealed class SymptomCheckInAuditLogger(
    ILogger<SymptomCheckInAuditLogger> logger) : ISymptomCheckInAuditLogger
{
    public void Recorded(
        EntityId actorAccountId,
        EntityId episodeId,
        EntityId packageVersionId,
        EntityId checkInId,
        int answerCount,
        PatientAccessReason accessReason,
        bool newlyCreated,
        DateTimeOffset createdAt) =>
        logger.LogInformation(
            "Symptom check-in {CheckInId} for episode {EpisodeId} and package " +
            "{PackageVersionId} was accepted for account {ActorAccountId} via " +
            "{AccessReason}; answer count {AnswerCount}, newly created {NewlyCreated}, " +
            "server time {CreatedAt}.",
            checkInId.Value,
            episodeId.Value,
            packageVersionId.Value,
            actorAccountId.Value,
            accessReason,
            answerCount,
            newlyCreated,
            createdAt);

    public void IdempotencyConflict(
        EntityId episodeId,
        EntityId packageVersionId,
        EntityId idempotencyKey,
        DateTimeOffset rejectedAt) =>
        logger.LogWarning(
            "Symptom check-in idempotency conflict for episode {EpisodeId}, package " +
            "{PackageVersionId}, key {IdempotencyKey} at {RejectedAt}.",
            episodeId.Value,
            packageVersionId.Value,
            idempotencyKey.Value,
            rejectedAt);
}
