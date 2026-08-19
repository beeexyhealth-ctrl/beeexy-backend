using System.Diagnostics;

namespace Beeexy.Api.Middleware;

public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    private const int MaximumCorrelationIdLength = 64;
    private const string LogPropertyName = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context.Request);

        context.TraceIdentifier = correlationId;
        context.Items[HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            [LogPropertyName] = correlationId
        });

        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "HTTP {RequestMethod} {RequestPath} started.",
            context.Request.Method,
            context.Request.Path.Value);

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            logger.LogInformation(
                "HTTP {RequestMethod} {RequestPath} completed with {StatusCode} in {ElapsedMilliseconds} ms.",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static string GetOrCreateCorrelationId(HttpRequest request)
    {
        if (request.Headers.TryGetValue(HeaderName, out var values) && values.Count == 1)
        {
            var candidate = values[0]?.Trim();

            if (candidate is { Length: > 0 and <= MaximumCorrelationIdLength }
                && candidate.All(IsValidCharacter))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsValidCharacter(char value)
    {
        return value is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '-'
            or '_'
            or '.'
            or ':';
    }
}
