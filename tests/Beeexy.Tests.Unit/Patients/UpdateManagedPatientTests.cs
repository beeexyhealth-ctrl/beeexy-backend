using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Patients;

public sealed class UpdateManagedPatientTests
{
    [Fact]
    public async Task PrimaryAccess_UpdatesApprovedDemographicAndVersion()
    {
        var fixture = new Fixture();

        var result = await fixture.UpdateAsync(
            fixture.ProfileFixture.PrimaryProfile.Id,
            Patch(firstName: "  Maria  "));

        Assert.Equal("Maria", result.FirstName);
        Assert.Equal(2, result.Version);
        Assert.Equal(PatientAccessReason.Primary, result.AuthorizationReason);
        Assert.Equal(1, fixture.Repository.SaveCount);
        Assert.True(fixture.Transaction.Committed);
        Assert.Equal(["firstName"], fixture.Audit.ChangedFields);
    }

    [Fact]
    public async Task ManagedAccess_UsesSharedAuthorizationAndUpdatesTarget()
    {
        var fixture = new Fixture();
        var target = fixture.AddManagedProfile();

        var result = await fixture.UpdateAsync(target.Id, Patch(state: "fl"));

        Assert.Equal("FL", result.State);
        Assert.Equal(PatientAccessReason.Managed, result.AuthorizationReason);
        Assert.Equal(fixture.ProfileFixture.PrimaryProfile.Id,
            fixture.AuthorizationRepository.RequestedManagerProfileId);
        Assert.Equal(target.Id, fixture.AuthorizationRepository.RequestedTargetProfileId);
    }

    [Fact]
    public async Task MultipleChangedFields_IncrementVersionOnlyOnce()
    {
        var fixture = new Fixture();
        var target = fixture.AddManagedProfile();

        var result = await fixture.UpdateAsync(
            target.Id,
            Patch(firstName: "Ana", lastName: "Vega", state: "CA"));

        Assert.Equal(2, result.Version);
        Assert.Equal(["firstName", "lastName", "state"], fixture.Audit.ChangedFields);
    }

    [Fact]
    public async Task SameValueUpdate_ReturnsCurrentStateWithoutSaveOrVersionIncrement()
    {
        var fixture = new Fixture();
        var target = fixture.AddManagedProfile();

        var result = await fixture.UpdateAsync(target.Id, Patch(firstName: "Maria"));

        Assert.Equal(1, result.Version);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.True(fixture.Transaction.Committed);
        Assert.Empty(fixture.Audit.ChangedFields);
    }

    [Fact]
    public async Task StaleVersion_ReturnsConflictWithoutMutation()
    {
        var fixture = new Fixture();
        var target = fixture.AddManagedProfile();

        await Assert.ThrowsAsync<ProfileUpdateConcurrencyException>(() =>
            fixture.UpdateAsync(target.Id, Patch(firstName: "Ana", version: 2)));

        Assert.Equal("Maria", target.FirstName?.Value);
        Assert.Equal(1, target.Version);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Equal(1, fixture.Audit.ConflictCount);
    }

    [Fact]
    public async Task PersistenceConcurrencyConflict_IsAuditedAndNotCommitted()
    {
        var fixture = new Fixture();
        var target = fixture.AddManagedProfile();
        fixture.Repository.SaveException = new ProfileUpdateConcurrencyException();

        await Assert.ThrowsAsync<ProfileUpdateConcurrencyException>(() =>
            fixture.UpdateAsync(target.Id, Patch(firstName: "Ana")));

        Assert.False(fixture.Transaction.Committed);
        Assert.Equal(1, fixture.Audit.ConflictCount);
    }

    [Fact]
    public async Task DeniedAndNonexistentTargets_UseSameConcealedNotFound()
    {
        var fixture = new Fixture();
        var denied = EntityId.New();
        var missing = EntityId.New();
        fixture.AuthorizationRepository.Set(denied, targetExists: true);
        fixture.AuthorizationRepository.Set(missing, targetExists: false);

        var deniedError = await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.UpdateAsync(denied, Patch(firstName: "Ana")));
        var missingError = await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.UpdateAsync(missing, Patch(firstName: "Ana")));

        Assert.Equal(deniedError.Message, missingError.Message);
        Assert.Equal(0, fixture.Repository.FindCount);
    }

    [Fact]
    public async Task RevokedRelationship_ProducesConcealedNotFound()
    {
        var fixture = new Fixture();
        var target = fixture.AddManagedProfile(authorize: false);
        fixture.AuthorizationRepository.Set(target.Id, targetExists: true);

        await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.UpdateAsync(target.Id, Patch(firstName: "Ana")));
    }

    [Fact]
    public async Task UnsupportedField_IsRejectedAfterAuthorization()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(
                fixture.ProfileFixture.PrimaryProfile.Id,
                Patch(firstName: "Ana") with { UnsupportedFields = ["accountId"] }));

        Assert.Equal("patient.unsupported_field", exception.Code);
        Assert.False(fixture.Transaction.Began);
    }

    [Fact]
    public async Task MissingVersion_IsRejected()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(
                fixture.ProfileFixture.PrimaryProfile.Id,
                Patch(firstName: "Ana") with { ExpectedVersion = null }));

        Assert.Equal("patient.invalid_version", exception.Code);
    }

    [Fact]
    public async Task NoDemographicFields_IsRejected()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(fixture.ProfileFixture.PrimaryProfile.Id, Patch()));

        Assert.Equal("patient.no_demographic_fields", exception.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvalidFirstName_IsRejected(string firstName)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(
                fixture.ProfileFixture.PrimaryProfile.Id,
                Patch(firstName: firstName)));

        Assert.Equal("patient.invalid_first_name", exception.Code);
    }

    [Theory]
    [InlineData("2026-08-22", "patient.invalid_date_of_birth")]
    [InlineData("08/20/2010", "patient.invalid_date_of_birth")]
    [InlineData("Unknown", "patient.invalid_sex_assigned_at_birth")]
    [InlineData("female", "patient.invalid_sex_assigned_at_birth")]
    [InlineData("XX", "patient.invalid_state")]
    public async Task InvalidDemographicValues_AreRejected(
        string value,
        string expectedCode)
    {
        var fixture = new Fixture();
        var patch = expectedCode switch
        {
            "patient.invalid_date_of_birth" => Patch(dateOfBirth: value),
            "patient.invalid_sex_assigned_at_birth" => Patch(sex: value),
            _ => Patch(state: value)
        };

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UpdateAsync(fixture.ProfileFixture.PrimaryProfile.Id, patch));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task ManagedUpdate_DoesNotMutateManagerTimezonePreference()
    {
        var fixture = new Fixture();
        var target = fixture.AddManagedProfile();
        var originalTimezone = fixture.ProfileFixture.Preference.TimeZone;
        var originalVersion = fixture.ProfileFixture.Preference.Version;

        await fixture.UpdateAsync(target.Id, Patch(lastName: "Vega"));

        Assert.Equal(originalTimezone, fixture.ProfileFixture.Preference.TimeZone);
        Assert.Equal(originalVersion, fixture.ProfileFixture.Preference.Version);
    }

    private static UpdateManagedPatientCommand Patch(
        string? firstName = null,
        string? lastName = null,
        string? dateOfBirth = null,
        string? sex = null,
        string? state = null,
        long? version = 1) =>
        new(
            version,
            Field(firstName),
            Field(lastName),
            Field(dateOfBirth),
            Field(sex),
            Field(state),
            []);

    private static PatientPatchField<string> Field(string? value) =>
        new(value is not null, value);

    private sealed class Fixture
    {
        public MyCircleListingTestFixture ProfileFixture { get; } = new();
        public FakeAuthorizationRepository AuthorizationRepository { get; } = new();
        public FakeUpdateRepository Repository { get; } = new();
        public FakeTransaction Transaction { get; } = new();
        public FakePatientAudit Audit { get; } = new();

        public Fixture()
        {
            Repository.Profiles[ProfileFixture.PrimaryProfile.Id] =
                ProfileFixture.PrimaryProfile;
        }

        public PatientProfile AddManagedProfile(bool authorize = true)
        {
            var profile = PatientProfile.CreateManaged(
                BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
                PatientName.Create("Maria"),
                PatientName.Create("Arias"),
                new DateOnly(2012, 5, 12),
                SexAssignedAtBirth.Female,
                UsState.Create("NY"),
                MyCircleListingTestFixture.Now);
            Repository.Profiles[profile.Id] = profile;
            if (authorize)
            {
                AuthorizationRepository.Set(profile.Id, true, EntityId.New());
            }

            return profile;
        }

        public Task<UpdateManagedPatientResult> UpdateAsync(
            EntityId targetProfileId,
            UpdateManagedPatientCommand command)
        {
            var authorizer = new AuthorizePatientAccess(
                new FakeClock(),
                ProfileFixture.Resolver,
                AuthorizationRepository,
                ProfileFixture.MyCircleAudit);
            var useCase = new UpdateManagedPatient(
                new FakeClock(),
                new FakeCurrentSessionIdentity(ProfileFixture.Account.Id),
                authorizer,
                Repository,
                Transaction,
                Audit);
            return useCase.ExecuteAsync(targetProfileId, command);
        }
    }

    private sealed class FakeAuthorizationRepository : IPatientAccessAuthorizationRepository
    {
        private readonly Dictionary<EntityId, PatientAccessAuthorizationLookup> _lookups = [];

        public EntityId? RequestedManagerProfileId { get; private set; }
        public EntityId? RequestedTargetProfileId { get; private set; }

        public void Set(EntityId targetProfileId, bool targetExists, EntityId? relationshipId = null) =>
            _lookups[targetProfileId] = new PatientAccessAuthorizationLookup(
                targetExists,
                relationshipId);

        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default)
        {
            RequestedManagerProfileId = managerProfileId;
            RequestedTargetProfileId = targetProfileId;
            return Task.FromResult(_lookups[targetProfileId]);
        }
    }

    private sealed class FakeUpdateRepository : IPatientProfileUpdateRepository
    {
        public Dictionary<EntityId, PatientProfile> Profiles { get; } = [];
        public int FindCount { get; private set; }
        public int SaveCount { get; private set; }
        public Exception? SaveException { get; set; }

        public Task<PatientProfile?> FindAsync(
            EntityId profileId,
            CancellationToken cancellationToken = default)
        {
            FindCount++;
            return Task.FromResult(Profiles.GetValueOrDefault(profileId));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return SaveException is null
                ? Task.CompletedTask
                : Task.FromException(SaveException);
        }
    }

    private sealed class FakeTransaction : IIdentityVerificationTransaction
    {
        public bool Began { get; private set; }
        public bool Committed { get; private set; }

        public Task BeginAsync(CancellationToken cancellationToken = default)
        {
            Began = true;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakePatientAudit : IPatientProfileAuditLogger
    {
        public IReadOnlyCollection<string> ChangedFields { get; private set; } = [];
        public int ConflictCount { get; private set; }

        public void UpdateSucceeded(
            EntityId actorAccountId,
            EntityId targetProfileId,
            PatientAccessReason accessReason,
            IReadOnlyCollection<string> changedFields,
            DateTimeOffset occurredAt) => ChangedFields = changedFields;

        public void UpdateConflict(
            EntityId actorAccountId,
            EntityId targetProfileId,
            PatientAccessReason accessReason) => ConflictCount++;
    }

    private sealed class FakeCurrentSessionIdentity(EntityId accountId)
        : ICurrentSessionIdentity
    {
        public CurrentSessionIdentity GetRequired() => new(accountId, EntityId.New());
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => MyCircleListingTestFixture.Now.AddMinutes(10);
    }
}
