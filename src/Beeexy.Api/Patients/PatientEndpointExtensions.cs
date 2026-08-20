using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Application.Patients;

namespace Beeexy.Api.Patients;

internal static class PatientEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexyPatientEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/patients/me",
                GetPrimaryProfileAsync)
            .WithName("GetPrimaryProfile")
            .WithTags("Patients")
            .WithDescription(
                "Returns the authenticated account's owned primary profile and current version.")
            .RequireAuthorization()
            .Produces<PrimaryProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPatch(
                "/api/v1/patients/me",
                UpdatePrimaryProfileAsync)
            .WithName("UpdatePrimaryProfile")
            .WithTags("Patients")
            .WithDescription(
                "Partially updates permitted profile preferences. The version from GET is required; " +
                "a stale version returns 409 without overwriting current state.")
            .RequireAuthorization()
            .Accepts<UpdatePrimaryProfileRequest>("application/json")
            .Produces<PrimaryProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> GetPrimaryProfileAsync(
        GetPrimaryProfile useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(cancellationToken);
        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> UpdatePrimaryProfileAsync(
        UpdatePrimaryProfileRequest request,
        UpdatePrimaryProfile useCase,
        CancellationToken cancellationToken)
    {
        if (request.UnsupportedFields is { Count: > 0 })
        {
            throw new RequestValidationException(
                "profile.unsupported_field",
                "The profile update contains an unsupported field.");
        }

        var result = await useCase.ExecuteAsync(
            new UpdatePrimaryProfileCommand(request.Timezone, request.Version),
            cancellationToken);
        return Results.Ok(ToResponse(result));
    }

    private static PrimaryProfileResponse ToResponse(PrimaryProfileResult result)
    {
        return new PrimaryProfileResponse(
            result.ProfileId.Value,
            result.BeeexyId,
            new PrimaryProfilePreferencesResponse(result.Timezone),
            result.Version);
    }
}

internal sealed record UpdatePrimaryProfileRequest(string? Timezone, long Version)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed record PrimaryProfileResponse(
    Guid ProfileId,
    string BeeexyId,
    PrimaryProfilePreferencesResponse Preferences,
    long Version);

internal sealed record PrimaryProfilePreferencesResponse(string Timezone);
