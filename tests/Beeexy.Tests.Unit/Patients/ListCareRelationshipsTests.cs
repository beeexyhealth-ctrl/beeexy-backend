using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Patients;

public sealed class ListCareRelationshipsTests
{
    [Fact]
    public async Task ActiveAndRevokedManagerHistory_AreBothIncluded()
    {
        var fixture = new MyCircleListingTestFixture();
        var active = fixture.Relationship(1);
        var revoked = fixture.Relationship(2, CareRelationshipStatus.Revoked);
        fixture.ReadRepository.RelationshipsByManager[fixture.PrimaryProfile.Id] =
            [active, revoked];

        var result = await fixture.CreateCareRelationshipsUseCase().ExecuteAsync();

        Assert.Equal(2, result.Relationships.Count);
        Assert.Contains(result.Relationships, value =>
            value.RelationshipId == active.RelationshipId &&
            value.Status == CareRelationshipStatus.Active);
        Assert.Contains(result.Relationships, value =>
            value.RelationshipId == revoked.RelationshipId &&
            value.Status == CareRelationshipStatus.Revoked &&
            value.RevokedAt is not null);
    }

    [Fact]
    public async Task Query_IsScopedToCurrentPrimaryManager()
    {
        var fixture = new MyCircleListingTestFixture();
        var own = fixture.Relationship(1);
        var unrelatedManagerId = EntityId.New();
        var unrelated = fixture.Relationship(2);
        fixture.ReadRepository.RelationshipsByManager[fixture.PrimaryProfile.Id] = [own];
        fixture.ReadRepository.RelationshipsByManager[unrelatedManagerId] = [unrelated];

        var result = await fixture.CreateCareRelationshipsUseCase().ExecuteAsync();

        Assert.Equal(fixture.PrimaryProfile.Id, fixture.ReadRepository.RequestedRelationshipManagerId);
        Assert.Equal(own.RelationshipId, Assert.Single(result.Relationships).RelationshipId);
    }

    [Fact]
    public async Task NoRelationships_ReturnsEmptyList()
    {
        var fixture = new MyCircleListingTestFixture();

        var result = await fixture.CreateCareRelationshipsUseCase().ExecuteAsync();

        Assert.Empty(result.Relationships);
    }

    [Fact]
    public async Task Relationships_AreOrderedByCreationThenRelationshipId()
    {
        var fixture = new MyCircleListingTestFixture();
        var lowerId = EntityId.From(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var higherId = EntityId.From(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var latest = fixture.Relationship(2);
        var second = fixture.Relationship(1, relationshipId: higherId);
        var first = fixture.Relationship(1, relationshipId: lowerId);
        fixture.ReadRepository.RelationshipsByManager[fixture.PrimaryProfile.Id] =
            [latest, second, first];

        var result = await fixture.CreateCareRelationshipsUseCase().ExecuteAsync();

        Assert.Equal(
            new[] { first.RelationshipId, second.RelationshipId, latest.RelationshipId },
            result.Relationships.Select(value => value.RelationshipId));
    }

    [Fact]
    public async Task DisabledAccount_FailsWithGenericAuthenticationFailure()
    {
        var fixture = new MyCircleListingTestFixture();
        fixture.Account.Disable(MyCircleListingTestFixture.Now.AddMinutes(1));

        await Assert.ThrowsAsync<SessionAuthenticationException>(() =>
            fixture.CreateCareRelationshipsUseCase().ExecuteAsync());
    }

    [Fact]
    public async Task MissingPrimaryProfile_FailsInvariantWithoutQueryingHistory()
    {
        var fixture = new MyCircleListingTestFixture();
        fixture.CurrentRepository.State = fixture.CurrentRepository.State with
        {
            Profiles = Array.Empty<PatientProfile>()
        };

        await Assert.ThrowsAsync<AccountProfileInvariantException>(() =>
            fixture.CreateCareRelationshipsUseCase().ExecuteAsync());

        Assert.Equal(["primary-profile-count"], fixture.ProfileAudit.InvariantNames);
        Assert.Null(fixture.ReadRepository.RequestedRelationshipManagerId);
    }
}
