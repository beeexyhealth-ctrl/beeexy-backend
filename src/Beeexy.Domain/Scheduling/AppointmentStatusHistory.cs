using Beeexy.Domain.Common;

namespace Beeexy.Domain.Scheduling;

public sealed class AppointmentStatusHistory
{
    private AppointmentStatusHistory()
    {
    }

    private AppointmentStatusHistory(
        EntityId id,
        EntityId appointmentId,
        long sequence,
        AppointmentStatus? previousStatus,
        AppointmentStatus newStatus,
        AppointmentActor actor,
        AppointmentStatusAction action,
        DateTimeOffset occurredAt)
    {
        Id = id;
        AppointmentId = appointmentId;
        Sequence = sequence;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        ActorAccountId = actor.AccountId;
        OperationalActorIdentifier = actor.OperationalIdentifier;
        ActorType = actor.Type;
        Action = action;
        OccurredAt = occurredAt;
    }

    public EntityId Id { get; private set; }

    public EntityId AppointmentId { get; private set; }

    public long Sequence { get; private set; }

    public AppointmentStatus? PreviousStatus { get; private set; }

    public AppointmentStatus NewStatus { get; private set; }

    public EntityId? ActorAccountId { get; private set; }

    public string? OperationalActorIdentifier { get; private set; }

    public AppointmentActorType ActorType { get; private set; }

    public AppointmentStatusAction Action { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    internal static AppointmentStatusHistory CreateInitial(
        EntityId appointmentId,
        EntityId actorAccountId,
        DateTimeOffset occurredAt)
    {
        return Create(
            appointmentId,
            1,
            null,
            AppointmentStatus.Requested,
            AppointmentActor.PatientAuthority(actorAccountId),
            AppointmentStatusAction.Creation,
            occurredAt);
    }

    internal static AppointmentStatusHistory CreateTransition(
        EntityId appointmentId,
        long sequence,
        AppointmentStatus previousStatus,
        AppointmentStatus newStatus,
        AppointmentActor actor,
        AppointmentStatusAction action,
        DateTimeOffset occurredAt)
    {
        return Create(
            appointmentId,
            sequence,
            previousStatus,
            newStatus,
            actor,
            action,
            occurredAt);
    }

    private static AppointmentStatusHistory Create(
        EntityId appointmentId,
        long sequence,
        AppointmentStatus? previousStatus,
        AppointmentStatus newStatus,
        AppointmentActor actor,
        AppointmentStatusAction action,
        DateTimeOffset occurredAt)
    {
        EnsureNonEmpty(appointmentId, nameof(appointmentId));
        ArgumentNullException.ThrowIfNull(actor);
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (!Enum.IsDefined(newStatus) ||
            (previousStatus.HasValue && !Enum.IsDefined(previousStatus.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(newStatus));
        }

        if (!Enum.IsDefined(actor.Type))
        {
            throw new ArgumentOutOfRangeException(nameof(actor));
        }

        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        InstantGuard.EnsureUtc(occurredAt, nameof(occurredAt));
        return new AppointmentStatusHistory(
            EntityId.New(),
            appointmentId,
            sequence,
            previousStatus,
            newStatus,
            actor,
            action,
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
