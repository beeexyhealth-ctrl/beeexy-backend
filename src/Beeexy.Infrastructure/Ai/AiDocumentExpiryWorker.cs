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
            var successful = await RunOnceAsync(stoppingToken);
            var delay = successful
                ? await GetDeadlineAwareDelaySafelyAsync(stoppingToken)
                : options.CleanupCadence;
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
            await scope.ServiceProvider.GetRequiredService<ExpireAiDocuments>()
                .ExecuteAsync(cancellationToken);
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
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var nextExpiry = await scope.ServiceProvider
            .GetRequiredService<IAiDocumentRepository>()
            .GetNextExpiryAsync(cancellationToken);
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

    private async Task<TimeSpan> GetDeadlineAwareDelaySafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetDeadlineAwareDelayAsync(cancellationToken);
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
