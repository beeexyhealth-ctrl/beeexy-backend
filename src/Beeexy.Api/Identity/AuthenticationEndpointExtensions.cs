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
}

internal sealed record RequestEmailChallengeRequest(string? Email);
