using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Api.Scheduling;

internal static class AppointmentEndpointExtensions
{
    private static readonly HashSet<string> SupportedListQueryParameters =
        ["patientId", "status", "from", "to", "cursor", "pageSize"];

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

        endpoints.MapGet("/api/v1/appointments", ListAsync)
            .WithName("ListAppointments")
            .WithTags("Scheduling")
            .WithDescription(
                "Lists appointments for the authenticated account's own and actively " +
                "managed patient profiles. Optional patientId, status, and half-open " +
                "[from,to) scheduled-start filters use opaque keyset pagination ordered " +
                "by start instant then appointment ID. The default page size is 20 and " +
                "the maximum is 100. Sensitive reason text is excluded from list items.")
            .RequireAuthorization()
            .Produces<AppointmentPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet("/api/v1/appointments/{id:guid}", GetAsync)
            .WithName("GetAppointment")
            .WithTags("Scheduling")
            .WithDescription(
                "Returns one currently authorized patient appointment with optional " +
                "reason, complete ordered status history, and a separate reschedule audit " +
                "projection. Missing and inaccessible appointments share concealed 404 " +
                "semantics. Related directory publication state does not hide history.")
            .RequireAuthorization()
            .Produces<AppointmentDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request,
        Guid? patientId,
        string? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? cursor,
        int? pageSize,
        ListAppointments useCase,
        CancellationToken cancellationToken)
    {
        ValidateListQueryParameters(request.Query);
        var result = await useCase.ExecuteAsync(
            new ListAppointmentsQuery(
                patientId.HasValue ? EntityId.From(patientId.Value) : null,
                status,
                from,
                to,
                cursor,
                pageSize),
            cancellationToken);
        return Results.Ok(new AppointmentPageResponse(
            result.Items.Select(ToSummaryResponse).ToArray(),
            result.NextCursor));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetAppointment useCase,
        CancellationToken cancellationToken)
    {
        var detail = await useCase.ExecuteAsync(EntityId.From(id), cancellationToken);
        var appointment = detail.Appointment;
        return Results.Ok(new AppointmentDetailResponse(
            appointment.AppointmentId.Value,
            appointment.PatientProfileId.Value,
            appointment.AvailabilitySlotId.Value,
            appointment.DoctorId.Value,
            appointment.ClinicId.Value,
            appointment.ClinicLocationId.Value,
            AppointmentStatuses.ToApiValue(appointment.Status),
            ToApiValue(appointment.Modality),
            appointment.StartsAt,
            appointment.EndsAt,
            appointment.ClinicTimeZone,
            detail.Reason,
            appointment.CreatedAt,
            detail.StatusHistory.Select(history => new AppointmentStatusHistoryResponse(
                history.Sequence,
                history.PreviousStatus.HasValue
                    ? AppointmentStatuses.ToApiValue(history.PreviousStatus.Value)
                    : null,
                AppointmentStatuses.ToApiValue(history.NewStatus),
                ToApiValue(history.ActorType),
                ToApiValue(history.Action),
                history.OccurredAt)).ToArray(),
            detail.RescheduleHistory.Select(history =>
                new AppointmentRescheduleHistoryResponse(
                    history.PreviousSlotId.Value,
                    history.NewSlotId.Value,
                    history.OccurredAt)).ToArray()));
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

    private static AppointmentSummaryResponse ToSummaryResponse(AppointmentSummary value) =>
        new(
            value.AppointmentId.Value,
            value.PatientProfileId.Value,
            value.AvailabilitySlotId.Value,
            value.DoctorId.Value,
            value.ClinicId.Value,
            value.ClinicLocationId.Value,
            AppointmentStatuses.ToApiValue(value.Status),
            ToApiValue(value.Modality),
            value.StartsAt,
            value.EndsAt,
            value.ClinicTimeZone,
            value.CreatedAt);

    private static string ToApiValue(AppointmentModality value) => value switch
    {
        AppointmentModality.InPerson => "inPerson",
        AppointmentModality.Virtual => "virtual",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string ToApiValue(AppointmentActorType value) => value switch
    {
        AppointmentActorType.PatientAuthority => "patientAuthority",
        AppointmentActorType.AppointmentScheduler => "appointmentScheduler",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string ToApiValue(AppointmentStatusAction value) => value switch
    {
        AppointmentStatusAction.Creation => "creation",
        AppointmentStatusAction.Confirmation => "confirmation",
        AppointmentStatusAction.Rejection => "rejection",
        AppointmentStatusAction.Cancellation => "cancellation",
        AppointmentStatusAction.Completion => "completion",
        AppointmentStatusAction.NoShow => "noShow",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static void ValidateListQueryParameters(IQueryCollection query)
    {
        if (query.Keys.Any(key => !SupportedListQueryParameters.Contains(key)))
        {
            throw new RequestValidationException(
                "scheduling.appointment_filter_unsupported",
                "The appointment list request contains an unsupported query parameter.");
        }

        if (query.Any(parameter => parameter.Value.Count != 1))
        {
            throw new RequestValidationException(
                "scheduling.appointment_filter_invalid",
                "Appointment list query parameters cannot be repeated.");
        }
    }

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

internal sealed record AppointmentPageResponse(
    IReadOnlyList<AppointmentSummaryResponse> Items,
    string? NextCursor);

internal sealed record AppointmentSummaryResponse(
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
    DateTimeOffset CreatedAt);

internal sealed record AppointmentDetailResponse(
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
    DateTimeOffset CreatedAt,
    IReadOnlyList<AppointmentStatusHistoryResponse> StatusHistory,
    IReadOnlyList<AppointmentRescheduleHistoryResponse> RescheduleHistory);

internal sealed record AppointmentStatusHistoryResponse(
    long Sequence,
    string? PreviousStatus,
    string NewStatus,
    string ActorType,
    string Action,
    DateTimeOffset OccurredAt);

internal sealed record AppointmentRescheduleHistoryResponse(
    Guid PreviousSlotId,
    Guid NewSlotId,
    DateTimeOffset OccurredAt);
