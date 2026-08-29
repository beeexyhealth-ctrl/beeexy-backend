using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

public sealed class DoctorSpecialty
{
    private DoctorSpecialty()
    {
    }

    private DoctorSpecialty(EntityId id, EntityId doctorId, EntityId specialtyId, DateTimeOffset createdAt)
    {
        Id = id;
        DoctorId = doctorId;
        SpecialtyId = specialtyId;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId DoctorId { get; private set; }

    public EntityId SpecialtyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static DoctorSpecialty Create(
        EntityId doctorId,
        EntityId specialtyId,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        DirectoryValueGuard.EnsureNonEmpty(doctorId, nameof(doctorId));
        DirectoryValueGuard.EnsureNonEmpty(specialtyId, nameof(specialtyId));
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        var entityId = id ?? EntityId.New();
        DirectoryValueGuard.EnsureNonEmpty(entityId, nameof(id));
        return new DoctorSpecialty(entityId, doctorId, specialtyId, createdAt);
    }
}
