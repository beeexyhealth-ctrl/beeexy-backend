using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Patients;

public sealed class AuthorizePatientAccessTests
{
    [Fact]
    public async Task OwnPrimaryProfile_IsAuthorized()
    {
        var fixture = new Fixture();

        var result = await fixture.AuthorizeAsync(fixture.ProfileFixture.PrimaryProfile.Id);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task PrimaryAccess_HasExplicitPrimaryReason()
    {
        var fixture = new Fixture();

        var result = await fixture.AuthorizeAsync(fixture.ProfileFixture.PrimaryProfile.Id);

        Assert.Equal(PatientAccessReason.Primary, result.Reason);
        Assert.Null(result.RelationshipId);
    }

    [Fact]
    public async Task PrimaryAccess_DoesNotRequireRelationshipLookup()
    {
        var fixture = new Fixture();

        await fixture.AuthorizeAsync(fixture.ProfileFixture.PrimaryProfile.Id);

        Assert.Equal(0, fixture.Repository.FindCount);
    }

    [Fact]
    public async Task AnotherAccountsPrimaryProfile_IsDenied()
    {
        var fixture = new Fixture();
        var targetId = EntityId.New();
        fixture.Repository.SetLookup(targetId, targetExists: true);

        var result = await fixture.AuthorizeAsync(targetId);

        AssertDenied(result);
        Assert.Equal(
            [PatientAccessDenialCategory.NoActiveManagementRelationship],
            fixture.ProfileFixture.MyCircleAudit.DenialCategories);
    }

    [Fact]
    public async Task ActiveRelationship_AuthorizesManagedAccess()
    {
        var fixture = new Fixture();
        var targetId = EntityId.New();
        var relationshipId = EntityId.New();
        fixture.Repository.SetLookup(targetId, true, relationshipId);

        var result = await fixture.AuthorizeAsync(targetId);

        Assert.True(result.IsAuthorized);
        Assert.Equal(relationshipId, result.RelationshipId);
    }

    [Fact]
    public async Task ManagedAccess_HasExplicitManagedReason()
    {
        var fixture = new Fixture();
        var targetId = EntityId.New();
        fixture.Repository.SetLookup(targetId, true, EntityId.New());

        var result = await fixture.AuthorizeAsync(targetId);

        Assert.Equal(PatientAccessReason.Managed, result.Reason);
    }

    [Fact]
    public async Task RevokedRelationship_DoesNotAuthorize()
    {
        var fixture = new Fixture();
        var targetId = EntityId.New();
        fixture.Repository.SetLookup(targetId, targetExists: true);

        var result = await fixture.AuthorizeAsync(targetId);

        AssertDenied(result);
    }

    [Fact]
    public async Task UnrelatedRelationship_DoesNotAuthorize()
    {
        var fixture = new Fixture();
        var targetId = EntityId.New();
        fixture.Repository.SetLookup(targetId, targetExists: true);

        var result = await fixture.AuthorizeAsync(targetId);

        AssertDenied(result);
        Assert.Equal(
            fixture.ProfileFixture.PrimaryProfile.Id,
            fixture.Repository.RequestedManagerProfileId);
        Assert.Equal(targetId, fixture.Repository.RequestedTargetProfileId);
    }

    [Fact]
    public async Task BeingRelationshipSubject_DoesNotGrantManagerAccess()
    {
        var fixture = new Fixture();
        var relationshipManagerTarget = EntityId.New();
        fixture.Repository.SetLookup(relationshipManagerTarget, targetExists: true);

        var result = await fixture.AuthorizeAsync(relationshipManagerTarget);

        AssertDenied(result);
    }

    [Fact]
    public async Task CreatorAccountIdentityAlone_DoesNotGrantAccess()
    {
        var fixture = new Fixture();
        var createdSubjectId = EntityId.New();
        fixture.Repository.SetLookup(createdSubjectId, targetExists: true);

        var result = await fixture.AuthorizeAsync(createdSubjectId);

        AssertDenied(result);
    }

    [Fact]
    public async Task NonexistentTarget_IsIndistinguishableFromUnauthorizedTarget()
    {
        var fixture = new Fixture();
        var missingTargetId = EntityId.New();
        var unauthorizedTargetId = EntityId.New();
        fixture.Repository.SetLookup(missingTargetId, targetExists: false);
        fixture.Repository.SetLookup(unauthorizedTargetId, targetExists: true);

        var missing = await fixture.AuthorizeAsync(missingTargetId);
        var unauthorized = await fixture.AuthorizeAsync(unauthorizedTargetId);

        AssertDenied(missing);
        AssertDenied(unauthorized);
        Assert.Equal(missing, unauthorized);
        Assert.Equal(
            new[]
            {
                PatientAccessDenialCategory.TargetNotFound,
                PatientAccessDenialCategory.NoActiveManagementRelationship
            },
            fixture.ProfileFixture.MyCircleAudit.DenialCategories);
    }

    [Fact]
    public async Task DisabledAccount_FailsWithGenericAuthenticationFailure()
    {
        var fixture = new Fixture();
        fixture.ProfileFixture.Account.Disable(MyCircleListingTestFixture.Now.AddMinutes(1));

        await Assert.ThrowsAsync<SessionAuthenticationException>(() =>
            fixture.AuthorizeAsync(EntityId.New()));

        Assert.Equal(0, fixture.Repository.FindCount);
    }

    [Fact]
    public async Task MissingPrimaryProfile_FailsExistingInvariantSafely()
    {
        var fixture = new Fixture();
        fixture.ProfileFixture.CurrentRepository.State =
            fixture.ProfileFixture.CurrentRepository.State with
            {
                Profiles = Array.Empty<PatientProfile>()
            };

        await Assert.ThrowsAsync<AccountProfileInvariantException>(() =>
            fixture.AuthorizeAsync(EntityId.New()));

        Assert.Equal(
            ["primary-profile-count"],
            fixture.ProfileFixture.ProfileAudit.InvariantNames);
        Assert.Equal(0, fixture.Repository.FindCount);
    }

    private static void AssertDenied(PatientAccessAuthorizationResult result)
    {
        Assert.False(result.IsAuthorized);
        Assert.Equal(PatientAccessReason.Denied, result.Reason);
        Assert.Null(result.RelationshipId);
    }

    private sealed class Fixture
    {
        public MyCircleListingTestFixture ProfileFixture { get; } = new();

        public FakeAuthorizationRepository Repository { get; } = new();

        public Task<PatientAccessAuthorizationResult> AuthorizeAsync(EntityId targetProfileId)
        {
            var useCase = new AuthorizePatientAccess(
                new FakeClock(),
                ProfileFixture.Resolver,
                Repository,
                ProfileFixture.MyCircleAudit);
            return useCase.ExecuteAsync(targetProfileId);
        }
    }

    private sealed class FakeAuthorizationRepository : IPatientAccessAuthorizationRepository
    {
        private readonly Dictionary<EntityId, PatientAccessAuthorizationLookup> _lookups = [];

        public int FindCount { get; private set; }

        public EntityId? RequestedManagerProfileId { get; private set; }

        public EntityId? RequestedTargetProfileId { get; private set; }

        public void SetLookup(
            EntityId targetProfileId,
            bool targetExists,
            EntityId? activeRelationshipId = null)
        {
            _lookups[targetProfileId] = new PatientAccessAuthorizationLookup(
                targetExists,
                activeRelationshipId);
        }

        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default)
        {
            FindCount++;
            RequestedManagerProfileId = managerProfileId;
            RequestedTargetProfileId = targetProfileId;
            return Task.FromResult(_lookups[targetProfileId]);
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => MyCircleListingTestFixture.Now.AddMinutes(2);
    }
}
