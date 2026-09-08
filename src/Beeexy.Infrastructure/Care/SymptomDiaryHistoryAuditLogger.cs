using Beeexy.Application.Care;
using Beeexy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Care;

internal sealed class SymptomDiaryHistoryAuditLogger(
    ILogger<SymptomDiaryHistoryAuditLogger> logger) : ISymptomDiaryHistoryAuditLogger
{
    public void HistoricalContentUnavailable(
        EntityId episodeId,
        int packageVersionCount) =>
        logger.LogWarning(
            "Symptom-diary history for episode {EpisodeId} failed closed because one of " +
            "{PackageVersionCount} referenced exact package versions was unavailable.",
            episodeId.Value,
            packageVersionCount);
}
