using Beeexy.Application.Ai;
using Beeexy.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Ai;

internal sealed class AiDocumentExpiryWorker(
    IServiceScopeFactory scopeFactory,
    AiDocumentOptions options,
    IClock clock,
    ILogger<AiDocumentExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var runCutoff = clock.UtcNow;
            var successful = await RunOnceAsync(stoppingToken);
            var delay = await GetNextDelayAfterRunSafelyAsync(
                successful,
                runCutoff,
                stoppingToken);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var expired = await scope.ServiceProvider.GetRequiredService<ExpireAiDocuments>()
                .ExecuteAsync(cancellationToken);
            logger.LogInformation(
                "Temporary AI document cleanup completed; removed artifact count " +
                "{RemovedArtifactCount}.",
                expired);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Temporary AI document expiry failed safely with category {FailureCategory}; " +
                "eligible metadata remains retryable.",
                exception.GetType().Name);
            return false;
        }
    }

    private async Task<TimeSpan> GetDeadlineAwareDelayAsync(
        bool previousRunSucceeded,
        DateTimeOffset previousRunCutoff,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var nextExpiry = await scope.ServiceProvider
            .GetRequiredService<IAiDocumentRepository>()
            .GetNextExpiryAsync(
                previousRunSucceeded ? null : previousRunCutoff,
                cancellationToken);
        if (!nextExpiry.HasValue)
        {
            return options.CleanupCadence;
        }

        var untilDeadline = nextExpiry.Value - clock.UtcNow;
        if (untilDeadline <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return untilDeadline < options.CleanupCadence
            ? untilDeadline
            : options.CleanupCadence;
    }

    internal async Task<TimeSpan> GetNextDelayAfterRunSafelyAsync(
        bool previousRunSucceeded,
        DateTimeOffset previousRunCutoff,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetDeadlineAwareDelayAsync(
                previousRunSucceeded,
                previousRunCutoff,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Temporary AI document expiry scheduling failed safely with category " +
                "{FailureCategory}; the fixed retry cadence remains active.",
                exception.GetType().Name);
            return options.CleanupCadence;
        }
    }
}
