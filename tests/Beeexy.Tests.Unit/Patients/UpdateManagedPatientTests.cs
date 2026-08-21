using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Patients;

public sealed class UpdateManagedPatientTests
{
    [Fact]
    public async Task PrimaryAccess_ReachesConservativeUpdateValidation()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(fixture.ProfileFixture.PrimaryProfile.Id));

        Assert.Equal("patient.no_mutable_fields", exception.Code);
        Assert.Equal(0, fixture.AuthorizationRepository.FindCount);
        Assert.Empty(fixture.ProfileFixture.MyCircleAudit.DenialCategories);
    }

    [Fact]
    public async Task ManagedAccess_ReachesConservativeUpdateValidation()
    {
        var fixture = new Fixture();
        var targetId = EntityId.New();
        fixture.AuthorizeManaged(targetId);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(targetId));

        Assert.Equal("patient.no_mutable_fields", exception.Code);
        Assert.Equal(1, fixture.AuthorizationRepository.FindCount);
    }

    [Fact]
    public async Task DeniedAccess_ProducesConcealableNotFound()
    {
        var fixture = new Fixture();
        var targetId = EntityId.New();
        fixture.AuthorizationRepository.Set(targetId, targetExists: true);

        await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.UpdateAsync(targetId));
    }

    [Fact]
    public async Task RevokedRelationship_ProducesConcealableNotFound()
    {
        var fixture = new Fixture();
        var targetId = EntityId.New();
        fixture.AuthorizationRepository.Set(targetId, targetExists: true);

        await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.UpdateAsync(targetId));
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
            fixture.UpdateAsync(missingId));
        var unauthorized = await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.UpdateAsync(unauthorizedId));

        Assert.Equal(missing.Message, unauthorized.Message);
    }

    [Fact]
    public async Task UnsupportedField_ProducesValidationFailureAfterAuthorization()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(
                fixture.ProfileFixture.PrimaryProfile.Id,
                "name"));

        Assert.Equal("patient.unsupported_field", exception.Code);
    }

    [Fact]
    public async Task OmittedFields_ProduceNoMutableFieldsFailureWithoutMutation()
    {
        var fixture = new Fixture();
        var before = fixture.ProfileFixture.PrimaryProfile;

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(before.Id));

        Assert.Equal("patient.no_mutable_fields", exception.Code);
        Assert.Equal(before.Id, fixture.ProfileFixture.PrimaryProfile.Id);
        Assert.Equal(before.BeeexyId, fixture.ProfileFixture.PrimaryProfile.BeeexyId);
        Assert.Null(fixture.ProfileFixture.PrimaryProfile.UpdatedAt);
    }

    [Theory]
    [InlineData("profileId")]
    [InlineData("accountId")]
    [InlineData("beeexyId")]
    [InlineData("relationshipType")]
    [InlineData("status")]
    public async Task ImmutableIdentifiersAndRelationshipFields_CannotBeChanged(string field)
    {
        var fixture = new Fixture();
        var profile = fixture.ProfileFixture.PrimaryProfile;

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(profile.Id, field));

        Assert.Equal("patient.unsupported_field", exception.Code);
        Assert.Equal(profile.Id, fixture.ProfileFixture.PrimaryProfile.Id);
        Assert.Equal(profile.AccountId, fixture.ProfileFixture.PrimaryProfile.AccountId);
        Assert.Equal(profile.BeeexyId, fixture.ProfileFixture.PrimaryProfile.BeeexyId);
    }

    [Fact]
    public async Task UseCase_DelegatesToSharedPhase34AuthorizationService()
    {
        var fixture = new Fixture();
        var targetId = EntityId.New();
        fixture.AuthorizeManaged(targetId);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(targetId));

        Assert.Equal(
            fixture.ProfileFixture.PrimaryProfile.Id,
            fixture.AuthorizationRepository.RequestedManagerProfileId);
        Assert.Equal(targetId, fixture.AuthorizationRepository.RequestedTargetProfileId);
    }

    [Fact]
    public async Task ManagedPatientUpdate_DoesNotMutateManagerPreference()
    {
        var fixture = new Fixture();
        var targetId = EntityId.New();
        fixture.AuthorizeManaged(targetId);
        var originalTimezone = fixture.ProfileFixture.Preference.TimeZone;
        var originalVersion = fixture.ProfileFixture.Preference.Version;

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(targetId, "timezone"));

        Assert.Equal(originalTimezone, fixture.ProfileFixture.Preference.TimeZone);
        Assert.Equal(originalVersion, fixture.ProfileFixture.Preference.Version);
    }

    [Fact]
    public async Task VersionField_IsNotAcceptedWithoutPatientLevelMutableState()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(fixture.ProfileFixture.PrimaryProfile.Id, "version"));

        Assert.Equal("patient.unsupported_field", exception.Code);
    }

    [Fact]
    public void CommandContract_IntroducesNoUnsupportedDemographics()
    {
        var property = Assert.Single(typeof(UpdateManagedPatientCommand).GetProperties());

        Assert.Equal("RequestedFields", property.Name);
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
            fixture.UpdateAsync(EntityId.New()));

        Assert.Equal(0, fixture.AuthorizationRepository.FindCount);
    }

    private sealed class Fixture
    {
        public MyCircleListingTestFixture ProfileFixture { get; } = new();

        public FakeAuthorizationRepository AuthorizationRepository { get; } = new();

        public void AuthorizeManaged(EntityId targetProfileId) =>
            AuthorizationRepository.Set(targetProfileId, true, EntityId.New());

        public Task UpdateAsync(EntityId targetProfileId, params string[] requestedFields)
        {
            var authorizer = new AuthorizePatientAccess(
                new FakeClock(),
                ProfileFixture.Resolver,
                AuthorizationRepository,
                ProfileFixture.MyCircleAudit);
            var useCase = new UpdateManagedPatient(authorizer);
            return useCase.ExecuteAsync(
                targetProfileId,
                new UpdateManagedPatientCommand(requestedFields));
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

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => MyCircleListingTestFixture.Now.AddMinutes(10);
    }
}
