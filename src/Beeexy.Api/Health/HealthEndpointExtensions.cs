using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Beeexy.Api.Health;

internal static class HealthEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexyHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/health/live",
                (HealthCheckService healthCheckService, HttpContext httpContext, CancellationToken cancellationToken) =>
                    WriteHealthResponseAsync(
                        healthCheckService,
                        httpContext,
                        HealthCheckTags.Live,
                        cancellationToken))
            .WithName("GetLiveness")
            .WithTags("Health")
            .WithSummary("Confirm application liveness")
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapGet(
                "/health/ready",
                (HealthCheckService healthCheckService, HttpContext httpContext, CancellationToken cancellationToken) =>
                    WriteHealthResponseAsync(
                        healthCheckService,
                        httpContext,
                        HealthCheckTags.Ready,
                        cancellationToken))
            .WithName("GetReadiness")
            .WithTags("Health")
            .WithSummary("Confirm PostgreSQL readiness")
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task WriteHealthResponseAsync(
        HealthCheckService healthCheckService,
        HttpContext httpContext,
        string tag,
        CancellationToken cancellationToken)
    {
        var report = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains(tag),
            cancellationToken);

        httpContext.Response.StatusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        httpContext.Response.Headers.CacheControl = "no-store, no-cache";

        await httpContext.Response.WriteAsJsonAsync(
            new HealthResponse(report.Status.ToString(), httpContext.TraceIdentifier),
            cancellationToken);
    }
}

internal sealed record HealthResponse(string Status, string CorrelationId);
