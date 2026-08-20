using Beeexy.Application.Identity;

namespace Beeexy.Api.Identity;

internal static class AuthenticationEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexyAuthenticationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/auth/email/challenges",
                RequestEmailChallengeAsync)
            .WithName("RequestEmailChallenge")
            .WithTags("Authentication")
            .Accepts<RequestEmailChallengeRequest>("application/json")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/auth/google",
                AuthenticateWithGoogleAsync)
            .WithName("AuthenticateWithGoogle")
            .WithTags("Authentication")
            .Accepts<GoogleAuthenticationRequest>("application/json")
            .Produces<AuthenticationTokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/auth/refresh",
                RotateRefreshSessionAsync)
            .WithName("RotateRefreshSession")
            .WithTags("Authentication")
            .Accepts<RefreshSessionRequest>("application/json")
            .Produces<AuthenticationTokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/auth/logout",
                LogoutSessionAsync)
            .WithName("LogoutSession")
            .WithTags("Authentication")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost(
                "/api/v1/auth/email/verify",
                VerifyEmailChallengeAsync)
            .WithName("VerifyEmailChallenge")
            .WithTags("Authentication")
            .Accepts<VerifyEmailChallengeRequest>("application/json")
            .Produces<AuthenticationTokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> RequestEmailChallengeAsync(
        RequestEmailChallengeRequest request,
        HttpContext httpContext,
        RequestEmailChallenge useCase,
        CancellationToken cancellationToken)
    {
        await useCase.ExecuteAsync(
            new RequestEmailChallengeCommand(
                request.Email,
                httpContext.Connection.RemoteIpAddress?.ToString()),
            cancellationToken);

        return Results.Accepted();
    }

    private static async Task<IResult> VerifyEmailChallengeAsync(
        VerifyEmailChallengeRequest request,
        VerifyEmailChallenge useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new VerifyEmailChallengeCommand(request.Email, request.Code),
            cancellationToken);

        return Results.Ok(ToResponse(
            result.Tokens,
            result.AccountId.Value,
            result.ProfileId.Value,
            result.BeeexyId));
    }

    private static async Task<IResult> RotateRefreshSessionAsync(
        RefreshSessionRequest request,
        RotateRefreshSession useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new RotateRefreshSessionCommand(request.RefreshToken),
            cancellationToken);

        return Results.Ok(ToResponse(
            result.Tokens,
            result.AccountId.Value,
            result.ProfileId.Value,
            result.BeeexyId));
    }

    private static async Task<IResult> AuthenticateWithGoogleAsync(
        GoogleAuthenticationRequest request,
        AuthenticateWithGoogle useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new AuthenticateWithGoogleCommand(request.Credential),
            cancellationToken);

        return Results.Ok(ToResponse(
            result.Tokens,
            result.AccountId.Value,
            result.ProfileId.Value,
            result.BeeexyId));
    }

    private static async Task<IResult> LogoutSessionAsync(
        LogoutSession useCase,
        CancellationToken cancellationToken)
    {
        await useCase.ExecuteAsync(cancellationToken);
        return Results.NoContent();
    }

    private static AuthenticationTokenResponse ToResponse(
        AuthenticationTokenPair tokens,
        Guid accountId,
        Guid profileId,
        string beeexyId)
    {
        return new AuthenticationTokenResponse(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.AccessTokenExpiresAt,
            tokens.RefreshTokenExpiresAt,
            new AccountSummaryResponse(accountId, profileId, beeexyId));
    }
}

internal sealed record RequestEmailChallengeRequest(string? Email);

internal sealed record VerifyEmailChallengeRequest(string? Email, string? Code);

internal sealed record RefreshSessionRequest(string? RefreshToken);

internal sealed record GoogleAuthenticationRequest(string? Credential);

internal sealed record AuthenticationTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt,
    AccountSummaryResponse Account);

internal sealed record AccountSummaryResponse(Guid AccountId, Guid ProfileId, string BeeexyId);
