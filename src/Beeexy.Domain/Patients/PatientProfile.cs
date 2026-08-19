using Beeexy.Domain.Common;

namespace Beeexy.Domain.Patients;

public sealed class PatientProfile
{
    private PatientProfile()
    {
        BeeexyId = null!;
    }

    private PatientProfile(
        EntityId id,
        EntityId? accountId,
        BeeexyId beeexyId,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        BeeexyId = beeexyId;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId? AccountId { get; private set; }

    public BeeexyId BeeexyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static PatientProfile Create(
        BeeexyId beeexyId,
        DateTimeOffset createdAt,
        EntityId? accountId = null,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(beeexyId);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));

        return new PatientProfile(id ?? EntityId.New(), accountId, beeexyId, createdAt);
    }
}
