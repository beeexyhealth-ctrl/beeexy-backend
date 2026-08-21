using Beeexy.Domain.Common;

namespace Beeexy.Domain.Patients;

public sealed class CareRelationship
{
    private CareRelationship()
    {
        Attestation = null!;
    }

    private CareRelationship(
        EntityId id,
        EntityId managerProfileId,
        EntityId subjectProfileId,
        CareRelationshipType relationshipType,
        EntityId createdByAccountId,
        AuthorizationAttestation attestation,
        DateTimeOffset createdAt)
    {
        Id = id;
        ManagerProfileId = managerProfileId;
        SubjectProfileId = subjectProfileId;
        RelationshipType = relationshipType;
        Status = CareRelationshipStatus.Active;
        CreatedByAccountId = createdByAccountId;
        Attestation = attestation;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId ManagerProfileId { get; private set; }

    public EntityId SubjectProfileId { get; private set; }

    public CareRelationshipType RelationshipType { get; private set; }

    public CareRelationshipStatus Status { get; private set; }

    public EntityId CreatedByAccountId { get; private set; }

    public AuthorizationAttestation Attestation { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public EntityId? RevokedByAccountId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static CareRelationship Create(
        EntityId managerProfileId,
        EntityId subjectProfileId,
        CareRelationshipType relationshipType,
        EntityId createdByAccountId,
        AuthorizationAttestation attestation,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(attestation);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        EnsureNonEmpty(managerProfileId, nameof(managerProfileId));
        EnsureNonEmpty(subjectProfileId, nameof(subjectProfileId));
        EnsureNonEmpty(createdByAccountId, nameof(createdByAccountId));

        if (managerProfileId == subjectProfileId)
        {
            throw new ArgumentException(
                "The manager and subject patient profiles must be different.",
                nameof(subjectProfileId));
        }

        if (!Enum.IsDefined(relationshipType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(relationshipType),
                "The care relationship type is not supported.");
        }

        if (attestation.AttestedAt > createdAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attestation),
                "The attestation timestamp cannot follow relationship creation.");
        }

        return new CareRelationship(
            id ?? EntityId.New(),
            managerProfileId,
            subjectProfileId,
            relationshipType,
            createdByAccountId,
            attestation,
            createdAt);
    }

    public void Revoke(EntityId revokedByAccountId, DateTimeOffset revokedAt)
    {
        EnsureNonEmpty(revokedByAccountId, nameof(revokedByAccountId));
        InstantGuard.EnsureNotBefore(revokedAt, CreatedAt, nameof(revokedAt));

        if (Status != CareRelationshipStatus.Active)
        {
            throw new InvalidOperationException(
                "Only an active care relationship can be revoked.");
        }

        Status = CareRelationshipStatus.Revoked;
        RevokedAt = revokedAt;
        RevokedByAccountId = revokedByAccountId;
        UpdatedAt = revokedAt;
    }

    private static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }
}
