using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Application.Patients;

namespace Beeexy.Api.Patients;

internal static class CareRelationshipEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexyCareRelationshipEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/care-relationships",
                CreateManagedPatientAsync)
            .WithName("CreateManagedPatient")
            .WithTags("Care Relationships")
            .WithDescription(
                "Creates a new unowned patient profile and an active management relationship " +
                "from the authenticated account's primary patient profile.")
            .RequireAuthorization()
            .Accepts<CreateManagedPatientRequest>("application/json")
            .Produces<CreateManagedPatientResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> CreateManagedPatientAsync(
        CreateManagedPatientRequest request,
        CreateManagedPatient useCase,
        CancellationToken cancellationToken)
    {
        if (request.UnsupportedFields is { Count: > 0 })
        {
            throw new RequestValidationException(
                "care_relationship.unsupported_field",
                "The care relationship request contains an unsupported field.");
        }

        var result = await useCase.ExecuteAsync(
            new CreateManagedPatientCommand(
                request.RelationshipType,
                request.AttestationVersion,
                request.AttestationAccepted),
            cancellationToken);

        var response = new CreateManagedPatientResponse(
            new CreatedCareRelationshipResponse(
                result.RelationshipId.Value,
                result.RelationshipType.ToString(),
                result.RelationshipStatus.ToString(),
                result.AttestationVersion,
                result.AttestedAt),
            new CreatedManagedPatientResponse(
                result.PatientProfileId.Value,
                result.BeeexyId));

        return Results.Created(
            $"/api/v1/patients/{result.PatientProfileId.Value}",
            response);
    }
}

internal sealed record CreateManagedPatientRequest(
    string? RelationshipType,
    string? AttestationVersion,
    bool AttestationAccepted)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed record CreateManagedPatientResponse(
    CreatedCareRelationshipResponse Relationship,
    CreatedManagedPatientResponse Patient);

internal sealed record CreatedCareRelationshipResponse(
    Guid Id,
    string Type,
    string Status,
    string AttestationVersion,
    DateTimeOffset AttestedAt);

internal sealed record CreatedManagedPatientResponse(Guid ProfileId, string BeeexyId);
