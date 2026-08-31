using Beeexy.Application.Common;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Application.Scheduling;

public sealed class ListAvailableSlots(
    IDoctorDirectoryReadRepository doctorRepository,
    IAvailabilitySlotReadRepository slotRepository,
    IClock clock)
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);
    public static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(90);

    public async Task<IReadOnlyList<AvailableSlot>> ExecuteAsync(
        EntityId doctorId,
        ListAvailableSlotsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.UtcNow.ToUniversalTime();
        var range = ResolveRange(query, now);

        if (await doctorRepository.GetAsync(doctorId, cancellationToken) is null)
        {
            throw new DoctorNotFoundException();
        }

        var effectiveFrom = range.From > now ? range.From : now;
        if (effectiveFrom >= range.To)
        {
            return [];
        }

        return await slotRepository.ListAvailableAsync(
            doctorId,
            effectiveFrom,
            range.To,
            now,
            cancellationToken);
    }

    public static AvailabilityQueryRange ResolveRange(
        ListAvailableSlotsQuery query,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(query);
        now = now.ToUniversalTime();
        var from = (query.From ?? now).ToUniversalTime();
        if (!query.To.HasValue && from > DateTimeOffset.MaxValue.Subtract(DefaultWindow))
        {
            throw InvalidRange("The availability range is outside the supported date range.");
        }

        var to = (query.To ?? from.Add(DefaultWindow)).ToUniversalTime();

        if (from >= to)
        {
            throw InvalidRange("The availability range start must be before its end.");
        }

        if (to - from > MaximumWindow)
        {
            throw InvalidRange("The availability range cannot exceed 90 days.");
        }

        return new AvailabilityQueryRange(from, to);
    }

    private static RequestValidationException InvalidRange(string message) =>
        new("availability.range_invalid", message);
}

public sealed record ListAvailableSlotsQuery(DateTimeOffset? From, DateTimeOffset? To);

public sealed record AvailabilityQueryRange(DateTimeOffset From, DateTimeOffset To);

public sealed record AvailableSlot(
    EntityId SlotId,
    EntityId DoctorId,
    EntityId ClinicId,
    EntityId LocationId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string ClinicTimeZone,
    AppointmentModality Modality);

public interface IAvailabilitySlotReadRepository
{
    Task<IReadOnlyList<AvailableSlot>> ListAvailableAsync(
        EntityId doctorId,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset futureCutoff,
        CancellationToken cancellationToken = default);
}
