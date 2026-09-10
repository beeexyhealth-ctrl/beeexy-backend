using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Sharing;

namespace Beeexy.Api.Sharing;

internal static class SharedAccessEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexySharedAccessEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/shared-access/exchange", ExchangeAsync)
            .WithName("ExchangeShareCapability")
            .WithTags("Shared Access")
            .WithDescription(
                "Exchanges a reusable active share capability from the JSON body for a " +
                "short-lived, read-only token bound to one ShareGrant. No account login or " +
                "shared health data is involved.")
            .AllowAnonymous()
            .RequireRateLimiting(ShareExchangeRateLimiting.PolicyName)
            .Accepts<ExchangeShareCapabilityRequest>("application/json")
            .Produces<ExchangeShareCapabilityResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        return endpoints;
    }

    private static async Task<IResult> ExchangeAsync(
        ExchangeShareCapabilityRequest request,
        HttpContext httpContext,
        ExchangeShareCapability useCase,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.Query.Count != 0 ||
            request.AdditionalFields is { Count: > 0 })
        {
            throw new ShareAccessDeniedException();
        }

        var result = await useCase.ExecuteAsync(request.Capability, cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new ExchangeShareCapabilityResponse(
            result.AccessToken,
            "Bearer",
            result.ExpiresAt));
    }
}

internal sealed record ExchangeShareCapabilityRequest
{
    public string? Capability { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record ExchangeShareCapabilityResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt);
