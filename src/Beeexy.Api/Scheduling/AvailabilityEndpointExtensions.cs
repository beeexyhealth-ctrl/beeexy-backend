using Beeexy.Application.Common;
using Beeexy.Application.Directory;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Api.Scheduling;

internal static class AvailabilityEndpointExtensions
{
    private static readonly HashSet<string> SupportedQueryParameters = ["from", "to"];

    public static IEndpointRouteBuilder MapBeeexyAvailabilityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/doctors/{doctorId:guid}/slots", ListAvailableSlotsAsync)
            .WithName("ListAvailableSlots")
            .WithTags("Scheduling")
            .WithDescription(
                "Lists a published doctor's published, future, currently unreserved slots " +
                "in chronological order. The optional from/to ISO-8601 instants use a " +
                "half-open [from,to) range; omitted boundaries default to a 30-day window " +
                "and no range may exceed 90 days. Requested and Confirmed appointments hide " +
                "a slot, while Cancelled and Rejected history does not. Returned UTC instants " +
                "and the explicit IANA clinic timezone support unambiguous local rendering. " +
                "Missing and unpublished doctors both return the same 404.")
            .AllowAnonymous()
            .Produces<AvailabilitySlotResponse[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> ListAvailableSlotsAsync(
        HttpRequest request,
        Guid doctorId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ListAvailableSlots useCase,
        CancellationToken cancellationToken)
    {
        ValidateQueryParameters(request.Query);
        if (doctorId == Guid.Empty)
        {
            throw new DoctorNotFoundException();
        }

        var slots = await useCase.ExecuteAsync(
            EntityId.From(doctorId),
            new ListAvailableSlotsQuery(from, to),
            cancellationToken);
        return Results.Ok(slots.Select(ToResponse).ToArray());
    }

    private static AvailabilitySlotResponse ToResponse(AvailableSlot slot) =>
        new(
            slot.SlotId.Value,
            slot.DoctorId.Value,
            slot.ClinicId.Value,
            slot.LocationId.Value,
            slot.StartsAt,
            slot.EndsAt,
            slot.ClinicTimeZone,
            ToCode(slot.Modality));

    private static string ToCode(AppointmentModality modality) => modality switch
    {
        AppointmentModality.InPerson => "inPerson",
        AppointmentModality.Virtual => "virtual",
        _ => throw new ArgumentOutOfRangeException(nameof(modality))
    };

    private static void ValidateQueryParameters(IQueryCollection query)
    {
        if (query.Keys.Any(key => !SupportedQueryParameters.Contains(key)))
        {
            throw new RequestValidationException(
                "availability.filter_unsupported",
                "The availability request contains an unsupported query parameter.");
        }

        if (query.Any(parameter => parameter.Value.Count != 1))
        {
            throw new RequestValidationException(
                "availability.range_invalid",
                "Availability range query parameters cannot be repeated.");
        }
    }
}

internal sealed record AvailabilitySlotResponse(
    Guid SlotId,
    Guid DoctorId,
    Guid ClinicId,
    Guid LocationId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string ClinicTimeZone,
    string Modality);
