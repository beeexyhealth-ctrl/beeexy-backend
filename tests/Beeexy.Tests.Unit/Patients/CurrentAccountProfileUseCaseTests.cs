using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Patients;

public sealed class CurrentAccountProfileUseCaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reads_ReturnOnlyOwnedAccountProfileAndPreferenceState()
    {
        var fixture = CreateFixture();

        var account = await new GetCurrentAccount(fixture.Resolver).ExecuteAsync();
        var profile = await new GetPrimaryProfile(fixture.Resolver).ExecuteAsync();

        Assert.Equal(fixture.Account.Id, account.AccountId);
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal(fixture.Profile.Id, account.PrimaryProfileId);
        Assert.Equal(fixture.Profile.BeeexyId.Value, account.BeeexyId);
        Assert.Equal("Etc/UTC", account.Timezone);
        Assert.Equal(fixture.Profile.Id, profile.ProfileId);
        Assert.Equal("Etc/UTC", profile.Timezone);
        Assert.Equal(1, profile.Version);
    }

    [Fact]
    public async Task MissingRequiredPreference_IsAuditedAndFailsAsInvariant()
    {
        var fixture = CreateFixture();
        fixture.Repository.State = fixture.Repository.State with
        {
            Preferences = Array.Empty<UserPreference>()
        };

        await Assert.ThrowsAsync<AccountProfileInvariantException>(
            () => fixture.Resolver.ResolveAsync());

        Assert.Equal(["preference-count"], fixture.Audit.InvariantNames);
    }

    [Fact]
    public async Task DisabledAccount_FailsWithGenericSessionAuthentication()
    {
        var fixture = CreateFixture();
        fixture.Account.Disable(Now);

        await Assert.ThrowsAsync<SessionAuthenticationException>(
            () => fixture.Resolver.ResolveAsync());
    }

    [Fact]
    public async Task UpdateTimezone_UsesPartialSemanticsAndAdvancesVersion()
    {
        var fixture = CreateFixture();

        var result = await fixture.CreateUpdateUseCase().ExecuteAsync(
            new UpdatePrimaryProfileCommand("America/Lima", 1));

        Assert.Equal("America/Lima", result.Timezone);
        Assert.Equal(2, result.Version);
        Assert.Equal(1, fixture.Repository.SaveCount);
        Assert.True(fixture.Transaction.Began);
        Assert.True(fixture.Transaction.Committed);
        Assert.Equal(["timezone"], fixture.Audit.SuccessfulFields.Single());
    }

    [Fact]
    public async Task OmittedTimezone_PreservesValueAndVersion()
    {
        var fixture = CreateFixture();

        var result = await fixture.CreateUpdateUseCase().ExecuteAsync(
            new UpdatePrimaryProfileCommand(null, 1));

        Assert.Equal("Etc/UTC", result.Timezone);
        Assert.Equal(1, result.Version);
        Assert.Empty(fixture.Audit.SuccessfulFields.Single());
    }

    [Theory]
    [InlineData("Not/A_Real_Zone", 1)]
    [InlineData("", 1)]
    [InlineData("America/Lima", 0)]
    public async Task InvalidUpdate_ReturnsValidationFailureBeforeTransaction(
        string timezone,
        long version)
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.CreateUpdateUseCase().ExecuteAsync(
                new UpdatePrimaryProfileCommand(timezone, version)));

        Assert.False(fixture.Transaction.Began);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task StaleVersion_DoesNotMutateOrSave()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<ProfileUpdateConcurrencyException>(() =>
            fixture.CreateUpdateUseCase().ExecuteAsync(
                new UpdatePrimaryProfileCommand("America/Lima", 2)));

        Assert.Equal("Etc/UTC", fixture.Preference.TimeZone.Value);
        Assert.Equal(1, fixture.Preference.Version);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Equal(1, fixture.Audit.ConflictCount);
    }

    private static Fixture CreateFixture()
    {
        var account = Account.Create(NormalizedEmail.Create("profile@example.com"), Now);
        var profile = PatientProfile.Create(
            BeeexyId.Create("BXY-PROFILE-TEST"),
            Now,
            account.Id);
        var preference = UserPreference.Create(
            account.Id,
            UserTimeZone.Create("Etc/UTC"),
            Now);
        var repository = new FakeRepository(new CurrentAccountProfileState(
            account,
            [profile],
            [preference]));
        var audit = new FakeAuditLogger();
        var resolver = new CurrentAccountProfileResolver(
            new FakeCurrentSessionIdentity(account.Id),
            repository,
            audit);
        return new Fixture(
            account,
            profile,
            preference,
            repository,
            audit,
            resolver,
            new FakeTransaction());
    }

    private sealed record Fixture(
        Account Account,
        PatientProfile Profile,
        UserPreference Preference,
        FakeRepository Repository,
        FakeAuditLogger Audit,
        CurrentAccountProfileResolver Resolver,
        FakeTransaction Transaction)
    {
        public UpdatePrimaryProfile CreateUpdateUseCase()
        {
            return new UpdatePrimaryProfile(
                new FakeClock(),
                Resolver,
                Repository,
                Transaction,
                Audit);
        }
    }

    private sealed class FakeCurrentSessionIdentity(EntityId accountId)
        : ICurrentSessionIdentity
    {
        public CurrentSessionIdentity GetRequired() =>
            new(accountId, EntityId.New());
    }

    private sealed class FakeRepository(CurrentAccountProfileState state)
        : ICurrentAccountProfileRepository
    {
        public CurrentAccountProfileState State { get; set; } = state;

        public int SaveCount { get; private set; }

        public Task<CurrentAccountProfileState> LoadAsync(
            EntityId accountId,
            CancellationToken cancellationToken = default) => Task.FromResult(State);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditLogger : IAccountProfileAuditLogger
    {
        public List<string> InvariantNames { get; } = [];

        public List<IReadOnlyCollection<string>> SuccessfulFields { get; } = [];

        public int ConflictCount { get; private set; }

        public void InvariantViolation(EntityId accountId, string invariant) =>
            InvariantNames.Add(invariant);

        public void ProfileUpdateSucceeded(
            EntityId accountId,
            EntityId profileId,
            IReadOnlyCollection<string> changedFields,
            DateTimeOffset occurredAt) => SuccessfulFields.Add(changedFields);

        public void ProfileUpdateConflict(EntityId accountId, EntityId profileId) =>
            ConflictCount++;
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

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Now.AddMinutes(1);
    }
}
