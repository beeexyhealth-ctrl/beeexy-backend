using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Domain;

public sealed class CareRelationshipTests
{
    [Fact]
    public void Create_PreservesRequiredDataAndBeginsActive()
    {
        var managerId = EntityId.New();
        var subjectId = EntityId.New();
        var creatorId = EntityId.New();
        var relationshipId = EntityId.New();
        var attestedAt = Utc(11);
        var createdAt = Utc(12);
        var attestation = AuthorizationAttestation.Create("draft-2026-08", attestedAt);

        var relationship = CareRelationship.Create(
            managerId,
            subjectId,
            CareRelationshipType.LegalGuardian,
            creatorId,
            attestation,
            createdAt,
            relationshipId);

        Assert.Equal(relationshipId, relationship.Id);
        Assert.Equal(managerId, relationship.ManagerProfileId);
        Assert.Equal(subjectId, relationship.SubjectProfileId);
        Assert.Equal(CareRelationshipType.LegalGuardian, relationship.RelationshipType);
        Assert.Equal(CareRelationshipStatus.Active, relationship.Status);
        Assert.Equal(creatorId, relationship.CreatedByAccountId);
        Assert.Same(attestation, relationship.Attestation);
        Assert.Equal("draft-2026-08", relationship.Attestation.Version);
        Assert.Equal(attestedAt, relationship.Attestation.AttestedAt);
        Assert.Equal(createdAt, relationship.CreatedAt);
        Assert.Null(relationship.RevokedAt);
        Assert.Null(relationship.RevokedByAccountId);
        Assert.Null(relationship.UpdatedAt);
    }

    [Theory]
    [InlineData(CareRelationshipType.Parent)]
    [InlineData(CareRelationshipType.LegalGuardian)]
    [InlineData(CareRelationshipType.Caregiver)]
    [InlineData(CareRelationshipType.Spouse)]
    [InlineData(CareRelationshipType.Child)]
    [InlineData(CareRelationshipType.Sibling)]
    [InlineData(CareRelationshipType.Other)]
    public void Create_AcceptsEveryApprovedRelationshipType(CareRelationshipType type)
    {
        var relationship = CreateRelationship(type);

        Assert.Equal(type, relationship.RelationshipType);
    }

    [Fact]
    public void Create_RejectsAnUnsupportedRelationshipType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateRelationship((CareRelationshipType)int.MaxValue));
    }

    [Fact]
    public void Create_RejectsSelfRelationship()
    {
        var profileId = EntityId.New();

        Assert.Throws<ArgumentException>(() => CareRelationship.Create(
            profileId,
            profileId,
            CareRelationshipType.Parent,
            EntityId.New(),
            AuthorizationAttestation.Create("draft", Utc(12)),
            Utc(12)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Attestation_RequiresVersion(string version)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            AuthorizationAttestation.Create(version, Utc(12)));
    }

    [Fact]
    public void Attestation_RejectsOverlongVersion()
    {
        var version = new string('v', AuthorizationAttestation.MaximumVersionLength + 1);

        Assert.Throws<ArgumentException>(() =>
            AuthorizationAttestation.Create(version, Utc(12)));
    }

    [Fact]
    public void Create_RequiresAttestationAndRejectsFutureAttestation()
    {
        var managerId = EntityId.New();
        var subjectId = EntityId.New();
        var creatorId = EntityId.New();

        Assert.Throws<ArgumentNullException>(() => CareRelationship.Create(
            managerId,
            subjectId,
            CareRelationshipType.Caregiver,
            creatorId,
            null!,
            Utc(12)));

        Assert.Throws<ArgumentOutOfRangeException>(() => CareRelationship.Create(
            managerId,
            subjectId,
            CareRelationshipType.Caregiver,
            creatorId,
            AuthorizationAttestation.Create("draft", Utc(13)),
            Utc(12)));
    }

    [Fact]
    public void Revoke_IsIrreversibleAndRecordsActorAndTimestamp()
    {
        var relationship = CreateRelationship();
        var revokerId = EntityId.New();
        var revokedAt = Utc(13);

        relationship.Revoke(revokerId, revokedAt);

        Assert.Equal(CareRelationshipStatus.Revoked, relationship.Status);
        Assert.Equal(revokedAt, relationship.RevokedAt);
        Assert.Equal(revokerId, relationship.RevokedByAccountId);
        Assert.Equal(revokedAt, relationship.UpdatedAt);

        Assert.Throws<InvalidOperationException>(() =>
            relationship.Revoke(revokerId, Utc(14)));
        Assert.Equal(revokedAt, relationship.RevokedAt);
        Assert.Equal(revokerId, relationship.RevokedByAccountId);
    }

    [Fact]
    public void Revoke_RejectsTimestampBeforeCreationWithoutChangingState()
    {
        var relationship = CreateRelationship();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            relationship.Revoke(EntityId.New(), Utc(11)));

        Assert.Equal(CareRelationshipStatus.Active, relationship.Status);
        Assert.Null(relationship.RevokedAt);
        Assert.Null(relationship.RevokedByAccountId);
    }

    [Fact]
    public void CareRelationship_HasNoBeeexyIdOrAuthorizationBehavior()
    {
        var publicProperties = typeof(CareRelationship).GetProperties();
        var publicMethods = typeof(CareRelationship).GetMethods();

        Assert.DoesNotContain(publicProperties, property =>
            property.PropertyType == typeof(BeeexyId));
        Assert.DoesNotContain(publicMethods, method =>
            method.Name.Contains("Authorize", StringComparison.OrdinalIgnoreCase) ||
            method.GetParameters().Any(parameter => parameter.ParameterType == typeof(BeeexyId)));
    }

    private static CareRelationship CreateRelationship(
        CareRelationshipType type = CareRelationshipType.Parent)
    {
        return CareRelationship.Create(
            EntityId.New(),
            EntityId.New(),
            type,
            EntityId.New(),
            AuthorizationAttestation.Create("draft", Utc(12)),
            Utc(12));
    }

    private static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(2026, 8, 20, hour, 0, 0, TimeSpan.Zero);
    }
}
