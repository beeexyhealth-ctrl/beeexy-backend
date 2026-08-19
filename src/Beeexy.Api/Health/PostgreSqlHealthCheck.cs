using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Beeexy.Api.Health;

internal static class HealthCheckTags
{
    public const string Live = "live";
    public const string Ready = "ready";
}

internal sealed class PostgreSqlHealthCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<PostgreSqlHealthCheck> logger) : IHealthCheck
{
    private const string UnavailableDescription = "PostgreSQL is unavailable.";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BeeexyDbContext>();

            if (await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Healthy();
            }
        }
        catch (Exception)
        {
            logger.LogWarning("PostgreSQL readiness check failed.");
            return HealthCheckResult.Unhealthy(UnavailableDescription);
        }

        logger.LogWarning("PostgreSQL readiness check reported unavailable.");
        return HealthCheckResult.Unhealthy(UnavailableDescription);
    }
}
