using Beeexy.Application.Triage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageCleanupOptions
{
    public PreTriageCleanupOptions(
        TimeSpan cadence,
        int batchSize,
        int maximumBatchesPerRun)
    {
        if (cadence <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cadence));
        }

        Cadence = cadence;
        Policy = new PreTriageCleanupPolicy(batchSize, maximumBatchesPerRun);
    }

    public TimeSpan Cadence { get; }

    public PreTriageCleanupPolicy Policy { get; }
}

internal sealed class PreTriageCleanupWorker(
    IServiceScopeFactory scopeFactory,
    PreTriageCleanupOptions options,
    ILogger<PreTriageCleanupWorker> logger) : BackgroundService
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
            var cleanup = scope.ServiceProvider.GetRequiredService<PreTriageCleanupService>();
            await cleanup.ExecuteAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Pre-triage cleanup run failed safely with category {FailureCategory}; " +
                "eligible records remain retryable.",
                exception.GetType().Name);
        }
    }
}
