using Microsoft.AspNetCore.Mvc;
using Beeexy.Application.Identity;

namespace Beeexy.Api.PrivateAccess;

internal sealed class PrivateAccessGateMiddleware(
    RequestDelegate next,
    ILogger<PrivateAccessGateMiddleware> logger)
{
    private static readonly PathString PrivateAccessRoot = "/api/v1/private-access";

    public async Task InvokeAsync(
        HttpContext context,
        PrivateAccessSettings settings,
        PrivateAccessSessionTokenService sessionTokenService,
        ResolvePrivateAccessSession databaseSessionResolver,
        IProblemDetailsService problemDetailsService)
    {
        if (!settings.Enabled || IsExempt(context.Request))
        {
            await next(context);
            return;
        }

        var token = context.Request.Cookies[PrivateAccessSettings.CookieName];
        var valid = settings.AuthenticationMode == PrivateAccessAuthenticationMode.Database
            ? await databaseSessionResolver.ExecuteAsync(token, context.RequestAborted) is not null
            : sessionTokenService.TryValidate(token, DateTimeOffset.UtcNow, out _);
        if (valid)
        {
            await next(context);
            return;
        }

        if (token is not null)
        {
            PrivateAccessEndpointExtensions.DeleteCookie(context, settings);
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.CacheControl = "no-store";
        logger.LogWarning(
            "Private access gate rejected a request to {RequestPath}.",
            context.Request.Path.Value);
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Private access required.",
            Detail = "A valid private demo access session is required.",
            Instance = context.Request.Path
        };
        problem.Extensions["correlationId"] = context.TraceIdentifier;
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem
        });
    }

    private static bool IsExempt(HttpRequest request)
    {
        if (HttpMethods.IsOptions(request.Method) ||
            !request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return request.Path.Equals(PrivateAccessRoot + "/login", StringComparison.OrdinalIgnoreCase) ||
            request.Path.Equals(PrivateAccessRoot + "/session", StringComparison.OrdinalIgnoreCase) ||
            request.Path.Equals(PrivateAccessRoot + "/logout", StringComparison.OrdinalIgnoreCase);
    }
}
