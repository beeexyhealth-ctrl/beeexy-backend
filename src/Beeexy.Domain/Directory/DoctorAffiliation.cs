using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

public sealed class DoctorAffiliation
{
    private DoctorAffiliation()
    {
    }

    private DoctorAffiliation(
        EntityId id,
        EntityId doctorId,
        EntityId clinicId,
        EntityId? clinicLocationId,
        bool isPublished,
        DateTimeOffset createdAt)
    {
        Id = id;
        DoctorId = doctorId;
        ClinicId = clinicId;
        ClinicLocationId = clinicLocationId;
        IsPublished = isPublished;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId DoctorId { get; private set; }

    public EntityId ClinicId { get; private set; }

    public EntityId? ClinicLocationId { get; private set; }

    public bool IsPublished { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static DoctorAffiliation Create(
        EntityId doctorId,
        EntityId clinicId,
        EntityId? clinicLocationId,
        bool isPublished,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        DirectoryValueGuard.EnsureNonEmpty(doctorId, nameof(doctorId));
        DirectoryValueGuard.EnsureNonEmpty(clinicId, nameof(clinicId));
        if (clinicLocationId.HasValue)
        {
            DirectoryValueGuard.EnsureNonEmpty(clinicLocationId.Value, nameof(clinicLocationId));
        }

        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        var entityId = id ?? EntityId.New();
        DirectoryValueGuard.EnsureNonEmpty(entityId, nameof(id));
        return new DoctorAffiliation(
            entityId,
            doctorId,
            clinicId,
            clinicLocationId,
            isPublished,
            createdAt);
    }
}
