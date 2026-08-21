using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Patients;

public sealed class ListAccessiblePatientsTests
{
    [Fact]
    public async Task NoRelationships_ReturnsPrimaryPatientOnly()
    {
        var fixture = new MyCircleListingTestFixture();

        var result = await fixture.CreateAccessiblePatientsUseCase().ExecuteAsync();

        var patient = Assert.Single(result.Patients);
        Assert.Equal(fixture.PrimaryProfile.Id, patient.ProfileId);
        Assert.Equal(fixture.PrimaryProfile.BeeexyId.Value, patient.BeeexyId);
        Assert.Equal(PatientAccessType.Primary, patient.AccessType);
        Assert.Null(patient.Relationship);
        Assert.Equal(
            fixture.PrimaryProfile.Id,
            fixture.ReadRepository.RequestedManagedPatientManagerId);
    }

    [Fact]
    public async Task ActiveManagedSubjects_AreIncludedWithRelationshipContext()
    {
        var fixture = new MyCircleListingTestFixture();
        var managed = fixture.ManagedPatient(1);
        fixture.ReadRepository.ManagedPatientsByManager[fixture.PrimaryProfile.Id] = [managed];

        var result = await fixture.CreateAccessiblePatientsUseCase().ExecuteAsync();

        Assert.Equal(2, result.Patients.Count);
        var patient = result.Patients[1];
        Assert.Equal(managed.ProfileId, patient.ProfileId);
        Assert.Equal(PatientAccessType.Managed, patient.AccessType);
        Assert.Equal(managed.RelationshipId, patient.Relationship?.RelationshipId);
        Assert.Equal(managed.RelationshipType, patient.Relationship?.RelationshipType);
    }

    [Fact]
    public async Task RevokedManagedSubjects_AreExcludedDefensively()
    {
        var fixture = new MyCircleListingTestFixture();
        fixture.ReadRepository.ManagedPatientsByManager[fixture.PrimaryProfile.Id] =
            [fixture.ManagedPatient(1, CareRelationshipStatus.Revoked)];

        var result = await fixture.CreateAccessiblePatientsUseCase().ExecuteAsync();

        Assert.Single(result.Patients);
        Assert.Equal(PatientAccessType.Primary, result.Patients[0].AccessType);
    }

    [Fact]
    public async Task DuplicateSubjectRows_AreDeduplicatedAndSafelyAudited()
    {
        var fixture = new MyCircleListingTestFixture();
        var subjectId = EntityId.New();
        var first = fixture.ManagedPatient(1, profileId: subjectId);
        var duplicate = fixture.ManagedPatient(2, profileId: subjectId);
        fixture.ReadRepository.ManagedPatientsByManager[fixture.PrimaryProfile.Id] =
            [duplicate, first];

        var result = await fixture.CreateAccessiblePatientsUseCase().ExecuteAsync();

        Assert.Equal(2, result.Patients.Count);
        Assert.Equal(first.RelationshipId, result.Patients[1].Relationship?.RelationshipId);
        Assert.Equal([subjectId], fixture.MyCircleAudit.DuplicateSubjectIds);
    }

    [Fact]
    public async Task ManagedPatients_AreOrderedByCreationThenRelationshipId()
    {
        var fixture = new MyCircleListingTestFixture();
        var lowerId = EntityId.From(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var higherId = EntityId.From(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var latest = fixture.ManagedPatient(2);
        var second = fixture.ManagedPatient(1, relationshipId: higherId);
        var first = fixture.ManagedPatient(1, relationshipId: lowerId);
        fixture.ReadRepository.ManagedPatientsByManager[fixture.PrimaryProfile.Id] =
            [latest, second, first];

        var result = await fixture.CreateAccessiblePatientsUseCase().ExecuteAsync();

        Assert.Equal(
            new[]
            {
                fixture.PrimaryProfile.Id,
                first.ProfileId,
                second.ProfileId,
                latest.ProfileId
            },
            result.Patients.Select(value => value.ProfileId));
    }

    [Fact]
    public async Task DisabledAccount_FailsWithGenericAuthenticationFailure()
    {
        var fixture = new MyCircleListingTestFixture();
        fixture.Account.Disable(MyCircleListingTestFixture.Now.AddMinutes(1));

        await Assert.ThrowsAsync<SessionAuthenticationException>(() =>
            fixture.CreateAccessiblePatientsUseCase().ExecuteAsync());
    }

    [Fact]
    public async Task MissingPrimaryProfile_FailsInvariantAndDoesNotReturnPartialState()
    {
        var fixture = new MyCircleListingTestFixture();
        fixture.CurrentRepository.State = fixture.CurrentRepository.State with
        {
            Profiles = Array.Empty<PatientProfile>()
        };

        await Assert.ThrowsAsync<AccountProfileInvariantException>(() =>
            fixture.CreateAccessiblePatientsUseCase().ExecuteAsync());

        Assert.Equal(["primary-profile-count"], fixture.ProfileAudit.InvariantNames);
        Assert.Null(fixture.ReadRepository.RequestedManagedPatientManagerId);
    }
}
