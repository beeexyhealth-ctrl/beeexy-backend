using Beeexy.Domain.Common;

namespace Beeexy.Domain.Scheduling;

public sealed class Appointment
{
    private readonly List<AppointmentStatusHistory> _statusHistory = [];

    private Appointment()
    {
        RequestFingerprint = null!;
    }

    private Appointment(
        EntityId id,
        EntityId patientProfileId,
        AvailabilitySlot slot,
        EntityId requestingAccountId,
        AppointmentModality modality,
        AppointmentReason? reason,
        EntityId idempotencyKey,
        AppointmentRequestFingerprint requestFingerprint,
        DateTimeOffset createdAt)
    {
        Id = id;
        PatientProfileId = patientProfileId;
        AvailabilitySlotId = slot.Id;
        ScheduledStartAt = slot.StartsAt;
        RequestingAccountId = requestingAccountId;
        Status = AppointmentStatus.Requested;
        Modality = modality;
        Reason = reason;
        IdempotencyKey = idempotencyKey;
        RequestFingerprint = requestFingerprint;
        Version = 1;
        CreatedAt = createdAt;
        _statusHistory.Add(AppointmentStatusHistory.CreateInitial(
            Id,
            requestingAccountId,
            createdAt));
    }

    public EntityId Id { get; private set; }

    public EntityId PatientProfileId { get; private set; }

    public EntityId AvailabilitySlotId { get; private set; }

    public DateTimeOffset ScheduledStartAt { get; private set; }

    public EntityId RequestingAccountId { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public AppointmentModality Modality { get; private set; }

    public AppointmentReason? Reason { get; private set; }

    public EntityId IdempotencyKey { get; private set; }

    public AppointmentRequestFingerprint RequestFingerprint { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public bool ReservesSlot => Status is AppointmentStatus.Requested or AppointmentStatus.Confirmed;

    public IReadOnlyCollection<AppointmentStatusHistory> StatusHistory =>
        _statusHistory.AsReadOnly();

    public static Appointment Create(
        EntityId patientProfileId,
        AvailabilitySlot slot,
        EntityId requestingAccountId,
        AppointmentModality modality,
        AppointmentReason? reason,
        EntityId idempotencyKey,
        AppointmentRequestFingerprint requestFingerprint,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        EnsureNonEmpty(patientProfileId, nameof(patientProfileId));
        ArgumentNullException.ThrowIfNull(slot);
        EnsureNonEmpty(slot.Id, nameof(slot));
        EnsureNonEmpty(requestingAccountId, nameof(requestingAccountId));
        EnsureNonEmpty(idempotencyKey, nameof(idempotencyKey));
        ArgumentNullException.ThrowIfNull(requestFingerprint);
        EnsureSupportedModality(modality);
        if (modality != slot.Modality)
        {
            throw new ArgumentException(
                "The appointment modality must match the selected slot.",
                nameof(modality));
        }

        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        var entityId = id ?? EntityId.New();
        EnsureNonEmpty(entityId, nameof(id));
        return new Appointment(
            entityId,
            patientProfileId,
            slot,
            requestingAccountId,
            modality,
            reason,
            idempotencyKey,
            requestFingerprint,
            createdAt);
    }

    public bool Confirm(EntityId actorAccountId, DateTimeOffset occurredAt)
    {
        if (Status == AppointmentStatus.Confirmed)
        {
            return false;
        }

        return Transition(
            AppointmentStatus.Requested,
            AppointmentStatus.Confirmed,
            actorAccountId,
            AppointmentActorType.AppointmentScheduler,
            AppointmentStatusAction.Confirmation,
            occurredAt);
    }

    public bool Reject(EntityId actorAccountId, DateTimeOffset occurredAt)
    {
        if (Status == AppointmentStatus.Rejected)
        {
            return false;
        }

        return Transition(
            AppointmentStatus.Requested,
            AppointmentStatus.Rejected,
            actorAccountId,
            AppointmentActorType.AppointmentScheduler,
            AppointmentStatusAction.Rejection,
            occurredAt);
    }

    public bool Cancel(EntityId actorAccountId, DateTimeOffset occurredAt)
    {
        if (Status == AppointmentStatus.Cancelled)
        {
            return false;
        }

        if (Status is not (AppointmentStatus.Requested or AppointmentStatus.Confirmed))
        {
            throw new InvalidOperationException(
                $"An appointment in {Status} status cannot be cancelled.");
        }

        return ApplyTransition(
            AppointmentStatus.Cancelled,
            actorAccountId,
            AppointmentActorType.PatientAuthority,
            AppointmentStatusAction.Cancellation,
            occurredAt);
    }

    private bool Transition(
        AppointmentStatus requiredStatus,
        AppointmentStatus newStatus,
        EntityId actorAccountId,
        AppointmentActorType actorType,
        AppointmentStatusAction action,
        DateTimeOffset occurredAt)
    {
        if (Status != requiredStatus)
        {
            throw new InvalidOperationException(
                $"An appointment in {Status} status cannot transition to {newStatus}.");
        }

        return ApplyTransition(newStatus, actorAccountId, actorType, action, occurredAt);
    }

    private bool ApplyTransition(
        AppointmentStatus newStatus,
        EntityId actorAccountId,
        AppointmentActorType actorType,
        AppointmentStatusAction action,
        DateTimeOffset occurredAt)
    {
        EnsureNonEmpty(actorAccountId, nameof(actorAccountId));
        InstantGuard.EnsureNotBefore(occurredAt, CreatedAt, nameof(occurredAt));
        var previousStatus = Status;
        Version = checked(Version + 1);
        Status = newStatus;
        UpdatedAt = occurredAt;
        _statusHistory.Add(AppointmentStatusHistory.CreateTransition(
            Id,
            Version,
            previousStatus,
            newStatus,
            actorAccountId,
            actorType,
            action,
            occurredAt));
        return true;
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
