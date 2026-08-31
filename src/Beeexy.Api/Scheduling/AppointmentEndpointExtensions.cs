using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Api.Scheduling;

internal static class AppointmentEndpointExtensions
{
    public static IEndpointRouteBuilder MapBeeexyAppointmentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/appointments", RequestAsync)
            .WithName("RequestAppointment")
            .WithTags("Scheduling")
            .WithDescription(
                "Requests one published future availability slot for the authenticated " +
                "account's own patient profile or an actively managed patient. The UUID " +
                "idempotency key is scoped to the account: an exact replay returns the " +
                "original appointment with 200, while first creation returns 201. " +
                "PostgreSQL reservation uniqueness prevents double booking.")
            .RequireAuthorization()
            .Accepts<RequestAppointmentRequest>("application/json")
            .Produces<RequestAppointmentResponse>(StatusCodes.Status201Created)
            .Produces<RequestAppointmentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> RequestAsync(
        RequestAppointmentRequest request,
        RequestAppointment useCase,
        CancellationToken cancellationToken)
    {
        if (request.AdditionalFields is { Count: > 0 })
        {
            throw new RequestValidationException(
                "scheduling.unsupported_field",
                "The appointment request contains an unsupported field.");
        }

        if (request.PatientId == Guid.Empty ||
            request.SlotId == Guid.Empty ||
            request.IdempotencyKey == Guid.Empty)
        {
            throw new RequestValidationException(
                "scheduling.identifiers_required",
                "Patient, slot, and idempotency identifiers are required.");
        }

        var modality = ParseModality(request.Modality);
        var result = await useCase.ExecuteAsync(
            new RequestAppointmentCommand(
                EntityId.From(request.PatientId),
                EntityId.From(request.SlotId),
                modality,
                request.Reason,
                EntityId.From(request.IdempotencyKey)),
            cancellationToken);
        var response = ToResponse(result.Appointment);
        return result.NewlyCreated
            ? Results.Created(
                $"/api/v1/appointments/{result.Appointment.AppointmentId.Value:D}",
                response)
            : Results.Ok(response);
    }

    private static AppointmentModality ParseModality(string? value) => value switch
    {
        "inPerson" => AppointmentModality.InPerson,
        "virtual" => AppointmentModality.Virtual,
        _ => throw new RequestValidationException(
            "scheduling.modality_invalid",
            "The modality must be either 'inPerson' or 'virtual'.")
    };

    private static RequestAppointmentResponse ToResponse(RequestedAppointment value) => new(
        value.AppointmentId.Value,
        value.PatientProfileId.Value,
        value.AvailabilitySlotId.Value,
        value.DoctorId.Value,
        value.ClinicId.Value,
        value.ClinicLocationId.Value,
        value.Status switch
        {
            AppointmentStatus.Requested => "Requested",
            _ => throw new InvalidOperationException(
                "A new appointment request must have Requested status.")
        },
        value.Modality switch
        {
            AppointmentModality.InPerson => "inPerson",
            AppointmentModality.Virtual => "virtual",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        },
        value.StartsAt,
        value.EndsAt,
        value.ClinicTimeZone,
        value.Reason,
        value.CreatedAt);
}

internal sealed record RequestAppointmentRequest
{
    public Guid PatientId { get; init; }

    public Guid SlotId { get; init; }

    public string? Modality { get; init; }

    public string? Reason { get; init; }

    public Guid IdempotencyKey { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

internal sealed record RequestAppointmentResponse(
    Guid AppointmentId,
    Guid PatientId,
    Guid SlotId,
    Guid DoctorId,
    Guid ClinicId,
    Guid LocationId,
    string Status,
    string Modality,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string ClinicTimeZone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason,
    DateTimeOffset CreatedAt);
