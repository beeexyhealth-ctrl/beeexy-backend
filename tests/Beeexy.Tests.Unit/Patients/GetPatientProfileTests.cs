using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Patients;

public sealed class GetPatientProfileTests
{
    [Fact]
    public async Task PrimaryAuthorization_ReturnsTargetProfile()
    {
        var fixture = new Fixture();
        fixture.AddReadable(fixture.ProfileFixture.PrimaryProfile);

        var result = await fixture.GetAsync(fixture.ProfileFixture.PrimaryProfile.Id);

        Assert.Equal(fixture.ProfileFixture.PrimaryProfile.Id, result.ProfileId);
        Assert.Equal(fixture.ProfileFixture.PrimaryProfile.BeeexyId.Value, result.BeeexyId);
        Assert.Equal(PatientAccessReason.Primary, result.AuthorizationReason);
    }

    [Fact]
    public async Task ManagedAuthorization_ReturnsIndependentTargetProfile()
    {
        var fixture = new Fixture();
        var target = fixture.CreateManagedProfile();
        fixture.AuthorizeManaged(target.Id);
        fixture.AddReadable(target);

        var result = await fixture.GetAsync(target.Id);

        Assert.Equal(target.Id, result.ProfileId);
        Assert.Equal(target.BeeexyId.Value, result.BeeexyId);
        Assert.Equal(PatientAccessReason.Managed, result.AuthorizationReason);
    }

    [Fact]
    public async Task DeniedAuthorization_ThrowsConcealableNotFoundWithoutReadingProfile()
    {
        var fixture = new Fixture();
        var targetId = EntityId.New();
        fixture.AuthorizationRepository.Set(targetId, targetExists: true);

        await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.GetAsync(targetId));

        Assert.Equal(0, fixture.ReadRepository.FindCount);
    }

    [Fact]
    public async Task NonexistentAndUnauthorizedTargets_UseSameNotFoundOutcome()
    {
        var fixture = new Fixture();
        var missingId = EntityId.New();
        var unauthorizedId = EntityId.New();
        fixture.AuthorizationRepository.Set(missingId, targetExists: false);
        fixture.AuthorizationRepository.Set(unauthorizedId, targetExists: true);

        var missing = await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.GetAsync(missingId));
        var unauthorized = await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.GetAsync(unauthorizedId));

        Assert.Equal(missing.Message, unauthorized.Message);
        Assert.Equal(0, fixture.ReadRepository.FindCount);
    }

    [Fact]
    public async Task RevokedRelationship_ProducesNotFound()
    {
        var fixture = new Fixture();
        var target = fixture.CreateManagedProfile();
        fixture.AuthorizationRepository.Set(target.Id, targetExists: true);
        fixture.AddReadable(target);

        await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.GetAsync(target.Id));

        Assert.Equal(0, fixture.ReadRepository.FindCount);
    }

    [Fact]
    public async Task UseCase_DelegatesAuthorizationToSharedPhase34Service()
    {
        var fixture = new Fixture();
        var target = fixture.CreateManagedProfile();
        fixture.AuthorizeManaged(target.Id);
        fixture.AddReadable(target);

        await fixture.GetAsync(target.Id);

        Assert.Equal(1, fixture.AuthorizationRepository.FindCount);
        Assert.Equal(
            fixture.ProfileFixture.PrimaryProfile.Id,
            fixture.AuthorizationRepository.RequestedManagerProfileId);
        Assert.Equal(target.Id, fixture.AuthorizationRepository.RequestedTargetProfileId);
        Assert.Equal(1, fixture.ReadRepository.FindCount);
    }

    [Fact]
    public void ResultContract_ContainsNoUnsupportedDemographics()
    {
        var propertyNames = typeof(GetPatientProfileResult)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "AuthorizationReason", "BeeexyId", "ProfileId" },
            propertyNames);
    }

    [Fact]
    public async Task ManagedPatient_DoesNotInheritManagerPreferences()
    {
        var fixture = new Fixture();
        var target = fixture.CreateManagedProfile();
        fixture.AuthorizeManaged(target.Id);
        fixture.AddReadable(target);

        var result = await fixture.GetAsync(target.Id);

        Assert.DoesNotContain(
            typeof(GetPatientProfileResult).GetProperties(),
            property => property.Name is "Preferences" or "Timezone" or "Version");
        Assert.DoesNotContain(fixture.ProfileFixture.Preference.TimeZone.Value, result.BeeexyId);
    }

    [Fact]
    public async Task MissingPrimaryProfile_PreservesSafeInvariantFailure()
    {
        var fixture = new Fixture();
        fixture.ProfileFixture.CurrentRepository.State =
            fixture.ProfileFixture.CurrentRepository.State with
            {
                Profiles = Array.Empty<PatientProfile>()
            };

        await Assert.ThrowsAsync<AccountProfileInvariantException>(() =>
            fixture.GetAsync(EntityId.New()));

        Assert.Equal(0, fixture.AuthorizationRepository.FindCount);
        Assert.Equal(0, fixture.ReadRepository.FindCount);
    }

    [Fact]
    public async Task AuthorizedTargetRemovedBeforeRead_StillProducesConcealableNotFound()
    {
        var fixture = new Fixture();
        var target = fixture.CreateManagedProfile();
        fixture.AuthorizeManaged(target.Id);

        await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.GetAsync(target.Id));

        Assert.Equal(1, fixture.ReadRepository.FindCount);
    }

    private sealed class Fixture
    {
        public MyCircleListingTestFixture ProfileFixture { get; } = new();

        public FakeAuthorizationRepository AuthorizationRepository { get; } = new();

        public FakePatientProfileReadRepository ReadRepository { get; } = new();

        public PatientProfile CreateManagedProfile() => PatientProfile.Create(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            MyCircleListingTestFixture.Now);

        public void AuthorizeManaged(EntityId targetProfileId) =>
            AuthorizationRepository.Set(targetProfileId, true, EntityId.New());

        public void AddReadable(PatientProfile profile) =>
            ReadRepository.Profiles[profile.Id] = new PatientProfileReadRecord(
                profile.Id,
                profile.BeeexyId.Value);

        public Task<GetPatientProfileResult> GetAsync(EntityId targetProfileId)
        {
            var authorizer = new AuthorizePatientAccess(
                new FakeClock(),
                ProfileFixture.Resolver,
                AuthorizationRepository,
                ProfileFixture.MyCircleAudit);
            var useCase = new GetPatientProfile(authorizer, ReadRepository);
            return useCase.ExecuteAsync(targetProfileId);
        }
    }

    private sealed class FakeAuthorizationRepository : IPatientAccessAuthorizationRepository
    {
        private readonly Dictionary<EntityId, PatientAccessAuthorizationLookup> _lookups = [];

        public int FindCount { get; private set; }

        public EntityId? RequestedManagerProfileId { get; private set; }

        public EntityId? RequestedTargetProfileId { get; private set; }

        public void Set(
            EntityId targetProfileId,
            bool targetExists,
            EntityId? activeRelationshipId = null) =>
            _lookups[targetProfileId] = new PatientAccessAuthorizationLookup(
                targetExists,
                activeRelationshipId);

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

    private sealed class FakePatientProfileReadRepository : IPatientProfileReadRepository
    {
        public Dictionary<EntityId, PatientProfileReadRecord> Profiles { get; } = [];

        public int FindCount { get; private set; }

        public Task<PatientProfileReadRecord?> FindAsync(
            EntityId profileId,
            CancellationToken cancellationToken = default)
        {
            FindCount++;
            return Task.FromResult(Profiles.GetValueOrDefault(profileId));
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => MyCircleListingTestFixture.Now.AddMinutes(5);
    }
}
