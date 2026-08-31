using Beeexy.Domain.Common;

namespace Beeexy.Domain.Scheduling;

public sealed class AppointmentRescheduleHistory
{
    private AppointmentRescheduleHistory()
    {
    }

    private AppointmentRescheduleHistory(
        EntityId id,
        EntityId appointmentId,
        EntityId previousSlotId,
        EntityId newSlotId,
        EntityId actorAccountId,
        DateTimeOffset occurredAt)
    {
        Id = id;
        AppointmentId = appointmentId;
        PreviousSlotId = previousSlotId;
        NewSlotId = newSlotId;
        ActorAccountId = actorAccountId;
        OccurredAt = occurredAt;
    }

    public EntityId Id { get; private set; }

    public EntityId AppointmentId { get; private set; }

    public EntityId PreviousSlotId { get; private set; }

    public EntityId NewSlotId { get; private set; }

    public EntityId ActorAccountId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static AppointmentRescheduleHistory Create(
        EntityId appointmentId,
        EntityId previousSlotId,
        EntityId newSlotId,
        EntityId actorAccountId,
        DateTimeOffset occurredAt,
        EntityId? id = null)
    {
        EnsureNonEmpty(appointmentId, nameof(appointmentId));
        EnsureNonEmpty(previousSlotId, nameof(previousSlotId));
        EnsureNonEmpty(newSlotId, nameof(newSlotId));
        EnsureNonEmpty(actorAccountId, nameof(actorAccountId));
        if (previousSlotId == newSlotId)
        {
            throw new ArgumentException(
                "A reschedule must identify distinct previous and new slots.",
                nameof(newSlotId));
        }

        InstantGuard.EnsureUtc(occurredAt, nameof(occurredAt));
        var entityId = id ?? EntityId.New();
        EnsureNonEmpty(entityId, nameof(id));
        return new AppointmentRescheduleHistory(
            entityId,
            appointmentId,
            previousSlotId,
            newSlotId,
            actorAccountId,
            occurredAt);
    }

    private static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }
}
