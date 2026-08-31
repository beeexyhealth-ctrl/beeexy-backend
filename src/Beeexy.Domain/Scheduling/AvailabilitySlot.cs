using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;

namespace Beeexy.Domain.Scheduling;

public sealed class AvailabilitySlot
{
    private AvailabilitySlot()
    {
        ClinicTimeZone = null!;
    }

    private AvailabilitySlot(
        EntityId id,
        EntityId doctorId,
        EntityId clinicId,
        EntityId clinicLocationId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        IanaTimeZone clinicTimeZone,
        AppointmentModality modality,
        bool isPublished,
        DateTimeOffset createdAt)
    {
        Id = id;
        DoctorId = doctorId;
        ClinicId = clinicId;
        ClinicLocationId = clinicLocationId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        ClinicTimeZone = clinicTimeZone;
        Modality = modality;
        IsPublished = isPublished;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId DoctorId { get; private set; }

    public EntityId ClinicId { get; private set; }

    public EntityId ClinicLocationId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public IanaTimeZone ClinicTimeZone { get; private set; }

    public AppointmentModality Modality { get; private set; }

    public bool IsPublished { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public TimeSpan Duration => EndsAt - StartsAt;

    public static AvailabilitySlot Create(
        EntityId doctorId,
        EntityId clinicId,
        EntityId clinicLocationId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        IanaTimeZone clinicTimeZone,
        AppointmentModality modality,
        bool isPublished,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        EnsureNonEmpty(doctorId, nameof(doctorId));
        EnsureNonEmpty(clinicId, nameof(clinicId));
        EnsureNonEmpty(clinicLocationId, nameof(clinicLocationId));
        ArgumentNullException.ThrowIfNull(clinicTimeZone);
        EnsureSupportedModality(modality);
        InstantGuard.EnsureUtc(startsAt, nameof(startsAt));
        InstantGuard.EnsureUtc(endsAt, nameof(endsAt));
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        if (endsAt <= startsAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endsAt),
                "The slot end instant must be after its start instant.");
        }

        var entityId = id ?? EntityId.New();
        EnsureNonEmpty(entityId, nameof(id));
        return new AvailabilitySlot(
            entityId,
            doctorId,
            clinicId,
            clinicLocationId,
            startsAt,
            endsAt,
            clinicTimeZone,
            modality,
            isPublished,
            createdAt);
    }

    public void SetPublication(bool isPublished, DateTimeOffset updatedAt)
    {
        InstantGuard.EnsureNotBefore(updatedAt, CreatedAt, nameof(updatedAt));
        if (IsPublished == isPublished)
        {
            return;
        }

        IsPublished = isPublished;
        UpdatedAt = updatedAt;
    }

    private static void EnsureSupportedModality(AppointmentModality modality)
    {
        if (!Enum.IsDefined(modality))
        {
            throw new ArgumentOutOfRangeException(nameof(modality));
        }
    }

    private static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }
}
