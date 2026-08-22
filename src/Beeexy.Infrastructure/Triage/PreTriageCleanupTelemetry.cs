using Beeexy.Application.Triage;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageCleanupTelemetry(
    ILogger<PreTriageCleanupTelemetry> logger) : IPreTriageCleanupTelemetry
{
    public void RunStarted(
        DateTimeOffset cutoff,
        int batchSize,
        int maximumBatches) =>
        logger.LogInformation(
            "Pre-triage cleanup run started at cutoff {Cutoff}; batch size {BatchSize}; " +
            "maximum batches {MaximumBatches}.",
            cutoff,
            batchSize,
            maximumBatches);

    public void RunCompleted(PreTriageCleanupResult result) =>
        logger.LogInformation(
            "Pre-triage cleanup run completed; batches {Batches}; selected {Selected}; " +
            "removed {Removed}; anonymous active removed {AnonymousActiveRemoved}; " +
            "anonymous completed unclaimed removed {AnonymousCompletedRemoved}; " +
            "authenticated abandoned removed {AuthenticatedAbandonedRemoved}; " +
            "already absent {AlreadyAbsent}; skipped after revalidation " +
            "{SkippedAfterRevalidation}; permanent records preserved " +
            "{PreservedPermanent}; duration milliseconds {DurationMilliseconds}.",
            result.Batches,
            result.Selected,
            result.Removed,
            result.AnonymousActiveRemoved,
            result.AnonymousCompletedUnclaimedRemoved,
            result.AuthenticatedAbandonedRemoved,
            result.AlreadyAbsent,
            result.SkippedAfterRevalidation,
            result.PreservedPermanent,
            result.Duration.TotalMilliseconds);
}
