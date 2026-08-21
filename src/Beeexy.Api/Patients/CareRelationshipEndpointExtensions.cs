using System.ComponentModel.DataAnnotations;
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
        endpoints.MapGet(
                "/api/v1/care-relationships",
                ListCareRelationshipsAsync)
            .WithName("ListCareRelationships")
            .WithTags("Care Relationships")
            .WithDescription(
                "Returns Active and Revoked relationship history where the authenticated " +
                "account's primary patient is the manager, ordered by creation time and ID.")
            .RequireAuthorization()
            .Produces<CareRelationshipsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

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

        endpoints.MapDelete(
                "/api/v1/care-relationships/{id:guid}",
                RevokeCareRelationshipAsync)
            .WithName("RevokeCareRelationship")
            .WithTags("Care Relationships")
            .WithDescription(
                "Irreversibly revokes a relationship owned by the authenticated account's " +
                "primary manager profile. Repeated revocation by that manager is idempotent.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> ListCareRelationshipsAsync(
        ListCareRelationships useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(cancellationToken);
        return Results.Ok(new CareRelationshipsResponse(
            result.Relationships
                .Select(relationship => new CareRelationshipResponse(
                    relationship.RelationshipId.Value,
                    new CareRelationshipSubjectResponse(
                        relationship.SubjectProfileId.Value,
                        relationship.SubjectBeeexyId,
                        relationship.SubjectFirstName,
                        relationship.SubjectLastName),
                    relationship.RelationshipType.ToString(),
                    relationship.Status.ToString(),
                    relationship.AttestationVersion,
                    relationship.AttestedAt,
                    relationship.CreatedAt,
                    relationship.RevokedAt))
                .ToArray()));
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

        if (request.Patient?.UnsupportedFields is { Count: > 0 })
        {
            throw new RequestValidationException(
                "patient.unsupported_field",
                "The managed patient contains an unsupported field.");
        }

        var result = await useCase.ExecuteAsync(
            new CreateManagedPatientCommand(
                request.RelationshipType,
                request.AttestationVersion,
                request.AttestationAccepted,
                request.Patient is null
                    ? null
                    : new ManagedPatientDemographicsCommand(
                        request.Patient.FirstName,
                        request.Patient.LastName,
                        request.Patient.DateOfBirth,
                        request.Patient.SexAssignedAtBirth,
                        request.Patient.State)),
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
                result.BeeexyId,
                result.FirstName,
                result.LastName,
                result.DateOfBirth,
                result.SexAssignedAtBirth.ToString(),
                result.State,
                result.Version));

        return Results.Created(
            $"/api/v1/patients/{result.PatientProfileId.Value}",
            response);
    }

    private static async Task<IResult> RevokeCareRelationshipAsync(
        Guid id,
        RevokeCareRelationship useCase,
        CancellationToken cancellationToken)
    {
        await useCase.ExecuteAsync(
            Beeexy.Domain.Common.EntityId.From(id),
            cancellationToken);
        return Results.NoContent();
    }
}

internal sealed record CareRelationshipsResponse(
    IReadOnlyList<CareRelationshipResponse> Relationships);

internal sealed record CareRelationshipResponse(
    Guid Id,
    CareRelationshipSubjectResponse Subject,
    string Type,
    string Status,
    string AttestationVersion,
    DateTimeOffset AttestedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

internal sealed record CareRelationshipSubjectResponse(
    Guid ProfileId,
    string BeeexyId,
    string? FirstName,
    string? LastName);

internal sealed record CreateManagedPatientRequest(
    string? RelationshipType,
    string? AttestationVersion,
    bool AttestationAccepted,
    ManagedPatientRequest? Patient)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed record ManagedPatientRequest(
    [property: Required, StringLength(100, MinimumLength = 1)] string? FirstName,
    [property: Required, StringLength(100, MinimumLength = 1)] string? LastName,
    [property: Required, DataType(DataType.Date)] string? DateOfBirth,
    [property: Required, AllowedValues("Male", "Female")] string? SexAssignedAtBirth,
    [property: Required, RegularExpression(PatientApiValidationPatterns.UsState)] string? State)
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

internal sealed record CreatedManagedPatientResponse(
    Guid ProfileId,
    string BeeexyId,
    string FirstName,
    string LastName,
    [property: DataType(DataType.Date)] DateOnly DateOfBirth,
    [property: AllowedValues("Male", "Female")] string SexAssignedAtBirth,
    [property: RegularExpression(PatientApiValidationPatterns.UsState)] string State,
    long Version);
