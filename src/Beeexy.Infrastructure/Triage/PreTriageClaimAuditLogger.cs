using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageClaimAuditLogger(
    ILogger<PreTriageClaimAuditLogger> logger) : IPreTriageClaimAuditLogger
{
    public void ClaimTransitioned(
        EntityId sessionId,
        EntityId episodeId,
        EntityId patientProfileId,
        DateTimeOffset claimedAt) =>
        logger.LogInformation(
            "Anonymous pre-triage claim transitioned for session {SessionId}, episode " +
            "{EpisodeId}, patient {PatientProfileId}, claimed at {ClaimedAt}.",
            sessionId.Value,
            episodeId.Value,
            patientProfileId.Value,
            claimedAt);
}
