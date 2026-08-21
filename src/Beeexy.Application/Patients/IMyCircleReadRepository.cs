using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public interface IMyCircleReadRepository
{
    Task<IReadOnlyList<ManagedPatientAccessRecord>> ListActiveManagedPatientsAsync(
        EntityId managerProfileId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CareRelationshipListRecord>> ListRelationshipsAsync(
        EntityId managerProfileId,
        CancellationToken cancellationToken = default);
}

public sealed record ManagedPatientAccessRecord(
    EntityId ProfileId,
    string BeeexyId,
    string? FirstName,
    string? LastName,
    EntityId RelationshipId,
    CareRelationshipType RelationshipType,
    CareRelationshipStatus RelationshipStatus,
    DateTimeOffset RelationshipCreatedAt);

public sealed record CareRelationshipListRecord(
    EntityId RelationshipId,
    EntityId SubjectProfileId,
    string SubjectBeeexyId,
    string? SubjectFirstName,
    string? SubjectLastName,
    CareRelationshipType RelationshipType,
    CareRelationshipStatus Status,
    string AttestationVersion,
    DateTimeOffset AttestedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);
