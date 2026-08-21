using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;

namespace Beeexy.Api.Patients;

internal static class PatientEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexyPatientEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/patients",
                ListAccessiblePatientsAsync)
            .WithName("ListAccessiblePatients")
            .WithTags("Patients")
            .WithDescription(
                "Returns the authenticated account's primary patient first, followed by " +
                "actively managed patients ordered by relationship creation time and ID.")
            .RequireAuthorization()
            .Produces<AccessiblePatientsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

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

        endpoints.MapGet(
                "/api/v1/patients/{patientId:guid}",
                GetPatientProfileAsync)
            .WithName("GetPatientProfile")
            .WithTags("Patients")
            .WithDescription(
                "Returns an authorized primary or actively managed patient profile. " +
                "Absent and unauthorized profiles both return a concealed 404.")
            .RequireAuthorization()
            .Produces<PatientProfileResponse>(StatusCodes.Status200OK)
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

    private static async Task<IResult> ListAccessiblePatientsAsync(
        ListAccessiblePatients useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(cancellationToken);
        return Results.Ok(new AccessiblePatientsResponse(
            result.Patients.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> GetPrimaryProfileAsync(
        GetPrimaryProfile useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(cancellationToken);
        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> GetPatientProfileAsync(
        Guid patientId,
        GetPatientProfile useCase,
        CancellationToken cancellationToken)
    {
        if (patientId == Guid.Empty)
        {
            throw new PatientProfileNotFoundException();
        }

        var result = await useCase.ExecuteAsync(
            EntityId.From(patientId),
            cancellationToken);
        return Results.Ok(new PatientProfileResponse(
            result.ProfileId.Value,
            result.BeeexyId));
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

    private static AccessiblePatientResponse ToResponse(AccessiblePatientSummary patient)
    {
        var relationship = patient.Relationship is null
            ? null
            : new AccessiblePatientRelationshipResponse(
                patient.Relationship.RelationshipId.Value,
                patient.Relationship.RelationshipType.ToString());

        return new AccessiblePatientResponse(
            patient.ProfileId.Value,
            patient.BeeexyId,
            patient.AccessType.ToString(),
            relationship);
    }
}

internal sealed record AccessiblePatientsResponse(
    IReadOnlyList<AccessiblePatientResponse> Patients);

internal sealed record AccessiblePatientResponse(
    Guid ProfileId,
    string BeeexyId,
    string AccessType,
    AccessiblePatientRelationshipResponse? Relationship);

internal sealed record AccessiblePatientRelationshipResponse(
    Guid RelationshipId,
    string Type);

internal sealed record PatientProfileResponse(Guid ProfileId, string BeeexyId);

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
