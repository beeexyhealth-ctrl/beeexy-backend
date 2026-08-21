using System.ComponentModel.DataAnnotations;
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
                "Returns the authenticated account's owned primary profile, approved demographics, " +
                "PatientProfile version, timezone preference, and preference version.")
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
                "/api/v1/patients/{patientId:guid}",
                UpdateManagedPatientAsync)
            .WithName("UpdateManagedPatient")
            .WithTags("Patients")
            .WithDescription(
                "Partially updates approved patient demographics after Primary or Managed access " +
                "authorization. A stale PatientProfile version returns 409.")
            .RequireAuthorization()
            .Accepts<UpdateManagedPatientRequest>("application/json")
            .Produces<PatientProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
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
            result.BeeexyId,
            result.FirstName,
            result.LastName,
            result.DateOfBirth,
            result.SexAssignedAtBirth?.ToString(),
            result.State,
            result.Version));
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

    private static async Task<IResult> UpdateManagedPatientAsync(
        Guid patientId,
        UpdateManagedPatientRequest request,
        UpdateManagedPatient useCase,
        CancellationToken cancellationToken)
    {
        if (patientId == Guid.Empty)
        {
            throw new PatientProfileNotFoundException();
        }

        var result = await useCase.ExecuteAsync(
            EntityId.From(patientId),
            new UpdateManagedPatientCommand(
                request.Version,
                new PatientPatchField<string>(request.FirstNameSpecified, request.FirstName),
                new PatientPatchField<string>(request.LastNameSpecified, request.LastName),
                new PatientPatchField<string>(request.DateOfBirthSpecified, request.DateOfBirth),
                new PatientPatchField<string>(
                    request.SexAssignedAtBirthSpecified,
                    request.SexAssignedAtBirth),
                new PatientPatchField<string>(request.StateSpecified, request.State),
                request.UnsupportedFields?.Keys.ToArray() ?? []),
            cancellationToken);
        return Results.Ok(new PatientProfileResponse(
            result.ProfileId.Value,
            result.BeeexyId,
            result.FirstName,
            result.LastName,
            result.DateOfBirth,
            result.SexAssignedAtBirth?.ToString(),
            result.State,
            result.Version));
    }

    private static PrimaryProfileResponse ToResponse(PrimaryProfileResult result)
    {
        return new PrimaryProfileResponse(
            result.ProfileId.Value,
            result.BeeexyId,
            result.FirstName,
            result.LastName,
            result.DateOfBirth,
            result.SexAssignedAtBirth?.ToString(),
            result.State,
            result.ProfileVersion,
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
            patient.FirstName,
            patient.LastName,
            patient.AccessType.ToString(),
            relationship);
    }
}

internal sealed record AccessiblePatientsResponse(
    IReadOnlyList<AccessiblePatientResponse> Patients);

internal sealed record AccessiblePatientResponse(
    Guid ProfileId,
    string BeeexyId,
    string? FirstName,
    string? LastName,
    string AccessType,
    AccessiblePatientRelationshipResponse? Relationship);

internal sealed record AccessiblePatientRelationshipResponse(
    Guid RelationshipId,
    string Type);

internal sealed record PatientProfileResponse(
    Guid ProfileId,
    string BeeexyId,
    string? FirstName,
    string? LastName,
    [property: DataType(DataType.Date)] DateOnly? DateOfBirth,
    [property: AllowedValues("Male", "Female")] string? SexAssignedAtBirth,
    [property: RegularExpression(PatientApiValidationPatterns.UsState)] string? State,
    long Version);

internal static class PatientApiValidationPatterns
{
    public const string UsState =
        "^(?i:AL|AK|AZ|AR|CA|CO|CT|DE|FL|GA|HI|ID|IL|IN|IA|KS|KY|LA|ME|MD|" +
        "MA|MI|MN|MS|MO|MT|NE|NV|NH|NJ|NM|NY|NC|ND|OH|OK|OR|PA|RI|SC|SD|" +
        "TN|TX|UT|VT|VA|WA|WV|WI|WY)$";
}

internal sealed class UpdateManagedPatientRequest
{
    private string? _firstName;
    private string? _lastName;
    private string? _dateOfBirth;
    private string? _sexAssignedAtBirth;
    private string? _state;

    [Range(1, long.MaxValue)]
    public long? Version { get; init; }

    [StringLength(100, MinimumLength = 1)]
    public string? FirstName
    {
        get => _firstName;
        init
        {
            _firstName = value;
            FirstNameSpecified = true;
        }
    }

    [StringLength(100, MinimumLength = 1)]
    public string? LastName
    {
        get => _lastName;
        init
        {
            _lastName = value;
            LastNameSpecified = true;
        }
    }

    [DataType(DataType.Date)]
    public string? DateOfBirth
    {
        get => _dateOfBirth;
        init
        {
            _dateOfBirth = value;
            DateOfBirthSpecified = true;
        }
    }

    [AllowedValues("Male", "Female")]
    public string? SexAssignedAtBirth
    {
        get => _sexAssignedAtBirth;
        init
        {
            _sexAssignedAtBirth = value;
            SexAssignedAtBirthSpecified = true;
        }
    }

    [RegularExpression(PatientApiValidationPatterns.UsState)]
    public string? State
    {
        get => _state;
        init
        {
            _state = value;
            StateSpecified = true;
        }
    }

    [JsonIgnore]
    public bool FirstNameSpecified { get; private set; }

    [JsonIgnore]
    public bool LastNameSpecified { get; private set; }

    [JsonIgnore]
    public bool DateOfBirthSpecified { get; private set; }

    [JsonIgnore]
    public bool SexAssignedAtBirthSpecified { get; private set; }

    [JsonIgnore]
    public bool StateSpecified { get; private set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed record UpdatePrimaryProfileRequest(string? Timezone, long Version)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? UnsupportedFields { get; init; }
}

internal sealed record PrimaryProfileResponse(
    Guid ProfileId,
    string BeeexyId,
    string? FirstName,
    string? LastName,
    [property: DataType(DataType.Date)] DateOnly? DateOfBirth,
    [property: AllowedValues("Male", "Female")] string? SexAssignedAtBirth,
    [property: RegularExpression(PatientApiValidationPatterns.UsState)] string? State,
    long ProfileVersion,
    PrimaryProfilePreferencesResponse Preferences,
    long Version);

internal sealed record PrimaryProfilePreferencesResponse(string Timezone);
