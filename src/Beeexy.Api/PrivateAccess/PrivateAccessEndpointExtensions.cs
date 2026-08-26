using Beeexy.Application.Common;

namespace Beeexy.Api.PrivateAccess;

internal static class PrivateAccessEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexyPrivateAccessEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/private-access")
            .WithTags("Private Access");

        group.MapPost("/login", Login)
            .WithName("LoginPrivateAccess")
            .WithSummary("Establish a private demo access session")
            .Accepts<PrivateAccessLoginRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/session", GetSession)
            .WithName("GetPrivateAccessSession")
            .WithSummary("Check the private demo access session")
            .Produces<PrivateAccessSessionStatusResponse>(StatusCodes.Status200OK);

        group.MapPost("/logout", Logout)
            .WithName("LogoutPrivateAccess")
            .WithSummary("Remove the private demo access session")
            .Produces(StatusCodes.Status204NoContent);

        return endpoints;
    }

    private static IResult Login(
        PrivateAccessLoginRequest request,
        HttpContext httpContext,
        PrivateAccessSettings settings,
        PrivateAccessCredentialValidator credentialValidator,
        PrivateAccessSessionTokenService sessionTokenService,
        InMemoryPrivateAccessRateLimiter rateLimiter,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Beeexy.PrivateAccess.Audit");
        if (!settings.Enabled)
        {
            return Results.NoContent();
        }

        var requesterIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var rateLimit = rateLimiter.TryAcquire(requesterIp, DateTimeOffset.UtcNow);
        if (!rateLimit.IsAllowed)
        {
            logger.LogWarning("Private access login was rate limited.");
            throw new RateLimitExceededException(rateLimit.RetryAfter);
        }

        if (!IsWellFormed(request))
        {
            logger.LogWarning("Private access login failed with category malformed_request.");
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request.",
                detail: "The private access request is invalid.");
        }

        if (!credentialValidator.Validate(request.Username!, request.Password!, request.Keyword!))
        {
            logger.LogWarning("Private access login failed with category credential_mismatch.");
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Private access denied.",
                detail: "The private access credentials are invalid.");
        }

        var session = sessionTokenService.Issue(DateTimeOffset.UtcNow);
        httpContext.Response.Cookies.Append(
            PrivateAccessSettings.CookieName,
            session.Token,
            CreateCookieOptions(settings, session.ExpiresAt));
        httpContext.Response.Headers.CacheControl = "no-store";
        logger.LogInformation("Private access login succeeded.");
        return Results.NoContent();
    }

    private static IResult GetSession(
        HttpContext httpContext,
        PrivateAccessSettings settings,
        PrivateAccessSessionTokenService sessionTokenService)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        if (!settings.Enabled)
        {
            return Results.Ok(new PrivateAccessSessionStatusResponse(true, null));
        }

        var token = httpContext.Request.Cookies[PrivateAccessSettings.CookieName];
        var authenticated = sessionTokenService.TryValidate(
            token,
            DateTimeOffset.UtcNow,
            out var expiresAt);
        if (!authenticated && token is not null)
        {
            DeleteCookie(httpContext, settings);
        }

        return Results.Ok(new PrivateAccessSessionStatusResponse(
            authenticated,
            authenticated ? expiresAt : null));
    }

    private static IResult Logout(
        HttpContext httpContext,
        PrivateAccessSettings settings,
        ILoggerFactory loggerFactory)
    {
        DeleteCookie(httpContext, settings);
        httpContext.Response.Headers.CacheControl = "no-store";
        loggerFactory.CreateLogger("Beeexy.PrivateAccess.Audit")
            .LogInformation("Private access logout completed.");
        return Results.NoContent();
    }

    internal static CookieOptions CreateCookieOptions(
        PrivateAccessSettings settings,
        DateTimeOffset? expiresAt = null)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = settings.SecureCookie,
            SameSite = settings.SecureCookie ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
            Expires = expiresAt,
            MaxAge = expiresAt is null ? null : expiresAt.Value - DateTimeOffset.UtcNow
        };
    }

    internal static void DeleteCookie(HttpContext context, PrivateAccessSettings settings)
    {
        context.Response.Cookies.Delete(
            PrivateAccessSettings.CookieName,
            CreateCookieOptions(settings));
    }

    private static bool IsWellFormed(PrivateAccessLoginRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.Username) &&
            request.Username.Length <= 128 &&
            !string.IsNullOrWhiteSpace(request.Password) &&
            request.Password.Length <= 512 &&
            !string.IsNullOrWhiteSpace(request.Keyword) &&
            request.Keyword.Length <= 512;
    }
}

internal sealed record PrivateAccessLoginRequest(
    string? Username,
    string? Password,
    string? Keyword);

internal sealed record PrivateAccessSessionStatusResponse(
    bool Authenticated,
    DateTimeOffset? ExpiresAt);
