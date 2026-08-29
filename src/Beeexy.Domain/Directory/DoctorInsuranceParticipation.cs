using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

public sealed class DoctorInsuranceParticipation
{
    private DoctorInsuranceParticipation()
    {
    }

    private DoctorInsuranceParticipation(
        EntityId id,
        EntityId doctorId,
        EntityId insurancePlanId,
        DateTimeOffset createdAt)
    {
        Id = id;
        DoctorId = doctorId;
        InsurancePlanId = insurancePlanId;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId DoctorId { get; private set; }

    public EntityId InsurancePlanId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static DoctorInsuranceParticipation Create(
        EntityId doctorId,
        EntityId insurancePlanId,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        DirectoryValueGuard.EnsureNonEmpty(doctorId, nameof(doctorId));
        DirectoryValueGuard.EnsureNonEmpty(insurancePlanId, nameof(insurancePlanId));
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        var entityId = id ?? EntityId.New();
        DirectoryValueGuard.EnsureNonEmpty(entityId, nameof(id));
        return new DoctorInsuranceParticipation(entityId, doctorId, insurancePlanId, createdAt);
    }
}
