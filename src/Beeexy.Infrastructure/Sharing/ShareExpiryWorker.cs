using Beeexy.Application.Sharing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Sharing;

public sealed class ShareExpiryOptions
{
    public ShareExpiryOptions(
        TimeSpan cadence,
        int batchSize,
        int maximumBatchesPerRun)
    {
        if (cadence <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cadence));
        }

        Cadence = cadence;
        Policy = new ShareExpiryPolicy(batchSize, maximumBatchesPerRun);
    }

    public TimeSpan Cadence { get; }

    public ShareExpiryPolicy Policy { get; }
}

internal sealed class ShareExpiryWorker(
    IServiceScopeFactory scopeFactory,
    ShareExpiryOptions options,
    ILogger<ShareExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Cadence);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ExpireShares>()
                .ExecuteAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Share expiry reconciliation failed safely with category " +
                "{FailureCategory}; eligible grants remain retryable.",
                exception.GetType().Name);
        }
    }
}

internal sealed class ShareExpiryTelemetry(ILogger<ShareExpiryTelemetry> logger)
    : IShareExpiryTelemetry
{
    public void RunStarted(DateTimeOffset cutoff, int batchSize, int maximumBatches)
    {
        logger.LogDebug(
            "Share expiry reconciliation started at {Cutoff}; batch size {BatchSize}, " +
            "maximum batches {MaximumBatches}.",
            cutoff,
            batchSize,
            maximumBatches);
    }

    public void RunCompleted(ExpireSharesResult result)
    {
        logger.LogInformation(
            "Share expiry reconciliation completed; batches {BatchCount}, selected " +
            "{SelectedCount}, reconciled {ReconciledCount}, already reconciled " +
            "{AlreadyReconciledCount}, skipped {SkippedCount}.",
            result.Batches,
            result.Selected,
            result.Reconciled,
            result.AlreadyReconciled,
            result.SkippedAfterRevalidation);
    }
}
