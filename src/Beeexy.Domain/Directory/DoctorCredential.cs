using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

public sealed class DoctorCredential
{
    private DoctorCredential()
    {
        Name = null!;
    }

    private DoctorCredential(
        EntityId id,
        EntityId doctorId,
        DirectoryName name,
        DoctorCredentialStatus status,
        DateTimeOffset createdAt)
    {
        Id = id;
        DoctorId = doctorId;
        Name = name;
        Status = status;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId DoctorId { get; private set; }

    public DirectoryName Name { get; private set; }

    public DoctorCredentialStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static DoctorCredential Create(
        EntityId doctorId,
        DirectoryName name,
        DoctorCredentialStatus status,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        DirectoryValueGuard.EnsureNonEmpty(doctorId, nameof(doctorId));
        ArgumentNullException.ThrowIfNull(name);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "The credential status is not supported.");
        }

        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        var entityId = id ?? EntityId.New();
        DirectoryValueGuard.EnsureNonEmpty(entityId, nameof(id));
        return new DoctorCredential(entityId, doctorId, name, status, createdAt);
    }
}
