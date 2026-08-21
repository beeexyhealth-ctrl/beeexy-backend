using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Patients;

public sealed class CreateManagedPatientTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidCreation_UsesAuthenticatedPrimaryProfileAndCreatesUnownedSubject()
    {
        var fixture = CreateFixture();

        var result = await fixture.UseCase.ExecuteAsync(new CreateManagedPatientCommand(
            "LegalGuardian",
            "draft-2026-08",
            true,
            ValidPatient()));

        var subject = Assert.IsType<PatientProfile>(fixture.Repository.Subject);
        var relationship = Assert.IsType<CareRelationship>(fixture.Repository.Relationship);
        Assert.Equal(result.PatientProfileId, subject.Id);
        Assert.Null(subject.AccountId);
        Assert.Equal(result.BeeexyId, subject.BeeexyId.Value);
        Assert.StartsWith("BXY-", subject.BeeexyId.Value);
        Assert.Equal("Maria", subject.FirstName?.Value);
        Assert.Equal("Arias", subject.LastName?.Value);
        Assert.Equal(new DateOnly(2012, 5, 12), subject.DateOfBirth);
        Assert.Equal(SexAssignedAtBirth.Female, subject.SexAssignedAtBirth);
        Assert.Equal("NY", subject.State?.Code);
        Assert.Equal(1, subject.Version);
        Assert.Equal(fixture.PrimaryProfile.Id, relationship.ManagerProfileId);
        Assert.Equal(subject.Id, relationship.SubjectProfileId);
        Assert.Equal(fixture.Account.Id, relationship.CreatedByAccountId);
        Assert.Equal(CareRelationshipType.LegalGuardian, relationship.RelationshipType);
        Assert.Equal(CareRelationshipStatus.Active, relationship.Status);
        Assert.Equal("draft-2026-08", relationship.Attestation.Version);
        Assert.Equal(Now, relationship.Attestation.AttestedAt);
        Assert.True(fixture.Transaction.Began);
        Assert.True(fixture.Transaction.Committed);
        Assert.Equal(1, fixture.Repository.SaveCount);
        Assert.Single(fixture.CareAudit.Successes);
    }

    [Theory]
    [InlineData("Parent", CareRelationshipType.Parent)]
    [InlineData("LegalGuardian", CareRelationshipType.LegalGuardian)]
    [InlineData("Caregiver", CareRelationshipType.Caregiver)]
    [InlineData("Spouse", CareRelationshipType.Spouse)]
    [InlineData("Child", CareRelationshipType.Child)]
    [InlineData("Sibling", CareRelationshipType.Sibling)]
    [InlineData("Other", CareRelationshipType.Other)]
    public async Task ApprovedRelationshipTypes_AreAccepted(
        string requestedType,
        CareRelationshipType expectedType)
    {
        var fixture = CreateFixture();

        var result = await fixture.UseCase.ExecuteAsync(new CreateManagedPatientCommand(
            requestedType,
            "draft",
            true,
            ValidPatient()));

        Assert.Equal(expectedType, result.RelationshipType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Friend")]
    [InlineData("0")]
    public async Task InvalidRelationshipType_FailsValidationBeforeTransaction(string? type)
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UseCase.ExecuteAsync(new CreateManagedPatientCommand(
                type,
                "draft",
                true,
                ValidPatient())));

        Assert.Equal("care_relationship.invalid_type", exception.Code);
        Assert.False(fixture.Transaction.Began);
        Assert.Null(fixture.Repository.Subject);
    }

    [Fact]
    public async Task MissingExplicitAttestationAcceptance_FailsValidation()
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UseCase.ExecuteAsync(new CreateManagedPatientCommand(
                "Parent",
                "draft",
                false,
                ValidPatient())));

        Assert.Equal("care_relationship.attestation_required", exception.Code);
        Assert.False(fixture.Transaction.Began);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingAttestationVersion_FailsValidation(string? version)
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.UseCase.ExecuteAsync(new CreateManagedPatientCommand(
                "Parent",
                version,
                true,
                ValidPatient())));

        Assert.Equal("care_relationship.invalid_attestation_version", exception.Code);
        Assert.False(fixture.Transaction.Began);
    }

    [Fact]
    public async Task DisabledAccount_FailsWithGenericAuthenticationAndCreatesNothing()
    {
        var fixture = CreateFixture();
        fixture.Account.Disable(Now);

        await Assert.ThrowsAsync<SessionAuthenticationException>(() =>
            fixture.UseCase.ExecuteAsync(ValidCommand()));

        Assert.True(fixture.Transaction.Began);
        Assert.False(fixture.Transaction.Committed);
        Assert.Null(fixture.Repository.Subject);
        Assert.Null(fixture.Repository.Relationship);
    }

    [Fact]
    public async Task MissingManagerProfile_FailsSafelyAndIsAudited()
    {
        var fixture = CreateFixture();
        fixture.CurrentRepository.State = fixture.CurrentRepository.State with
        {
            Profiles = Array.Empty<PatientProfile>()
        };

        await Assert.ThrowsAsync<AccountProfileInvariantException>(() =>
            fixture.UseCase.ExecuteAsync(ValidCommand()));

        Assert.Equal(["primary-profile-count"], fixture.ProfileAudit.InvariantNames);
        Assert.Null(fixture.Repository.Subject);
    }

    [Fact]
    public async Task ExpectedPersistenceConflict_IsAuditedAndNotCommitted()
    {
        var fixture = CreateFixture();
        fixture.Repository.SaveException = new ManagedPatientCreationConflictException();

        await Assert.ThrowsAsync<ManagedPatientCreationConflictException>(() =>
            fixture.UseCase.ExecuteAsync(ValidCommand()));

        Assert.True(fixture.Transaction.Began);
        Assert.False(fixture.Transaction.Committed);
        Assert.Equal(1, fixture.CareAudit.ConflictCount);
    }

    [Fact]
    public async Task CreationAddsOnlyPatientAndRelationship_NoAccountOrSessionState()
    {
        var fixture = CreateFixture();

        await fixture.UseCase.ExecuteAsync(ValidCommand());

        Assert.IsType<PatientProfile>(fixture.Repository.Subject);
        Assert.IsType<CareRelationship>(fixture.Repository.Relationship);
        Assert.Null(fixture.Repository.Subject!.AccountId);
        Assert.Empty(fixture.Repository.AdditionalEntities);
    }

    private static CreateManagedPatientCommand ValidCommand() =>
        new("Child", "draft", true, ValidPatient());

    private static ManagedPatientDemographicsCommand ValidPatient() =>
        new("Maria", "Arias", "2012-05-12", "Female", "NY");

    private static Fixture CreateFixture()
    {
        var account = Account.Create(
            NormalizedEmail.Create("manager@example.com"),
            Now.AddMinutes(-1));
        var profile = PatientProfile.Create(
            BeeexyId.Create("BXY-MANAGER-TEST"),
            Now.AddMinutes(-1),
            account.Id);
        var preference = UserPreference.Create(
            account.Id,
            UserTimeZone.Create("Etc/UTC"),
            Now.AddMinutes(-1));
        var currentRepository = new FakeCurrentRepository(new CurrentAccountProfileState(
            account,
            [profile],
            [preference]));
        var profileAudit = new FakeProfileAuditLogger();
        var resolver = new CurrentAccountProfileResolver(
            new FakeCurrentSessionIdentity(account.Id),
            currentRepository,
            profileAudit);
        var repository = new FakeManagedPatientRepository();
        var transaction = new FakeTransaction();
        var careAudit = new FakeCareRelationshipAuditLogger();
        var useCase = new CreateManagedPatient(
            new FakeClock(),
            resolver,
            repository,
            transaction,
            careAudit);
        return new Fixture(
            account,
            profile,
            currentRepository,
            profileAudit,
            repository,
            transaction,
            careAudit,
            useCase);
    }

    private sealed record Fixture(
        Account Account,
        PatientProfile PrimaryProfile,
        FakeCurrentRepository CurrentRepository,
        FakeProfileAuditLogger ProfileAudit,
        FakeManagedPatientRepository Repository,
        FakeTransaction Transaction,
        FakeCareRelationshipAuditLogger CareAudit,
        CreateManagedPatient UseCase);

    private sealed class FakeCurrentSessionIdentity(EntityId accountId)
        : ICurrentSessionIdentity
    {
        public CurrentSessionIdentity GetRequired() => new(accountId, EntityId.New());
    }

    private sealed class FakeCurrentRepository(CurrentAccountProfileState state)
        : ICurrentAccountProfileRepository
    {
        public CurrentAccountProfileState State { get; set; } = state;

        public Task<CurrentAccountProfileState> LoadAsync(
            EntityId accountId,
            CancellationToken cancellationToken = default) => Task.FromResult(State);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeManagedPatientRepository : IManagedPatientCreationRepository
    {
        public PatientProfile? Subject { get; private set; }

        public CareRelationship? Relationship { get; private set; }

        public List<object> AdditionalEntities { get; } = [];

        public Exception? SaveException { get; set; }

        public int SaveCount { get; private set; }

        public void Add(PatientProfile subject, CareRelationship relationship)
        {
            Subject = subject;
            Relationship = relationship;
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

    private sealed class FakeProfileAuditLogger : IAccountProfileAuditLogger
    {
        public List<string> InvariantNames { get; } = [];

        public void InvariantViolation(EntityId accountId, string invariant) =>
            InvariantNames.Add(invariant);

        public void ProfileUpdateSucceeded(
            EntityId accountId,
            EntityId profileId,
            IReadOnlyCollection<string> changedFields,
            DateTimeOffset occurredAt)
        {
        }

        public void ProfileUpdateConflict(EntityId accountId, EntityId profileId)
        {
        }
    }

    private sealed class FakeCareRelationshipAuditLogger : ICareRelationshipAuditLogger
    {
        public List<EntityId> Successes { get; } = [];

        public int ConflictCount { get; private set; }

        public void CreationSucceeded(
            EntityId creatorAccountId,
            EntityId managerProfileId,
            EntityId subjectProfileId,
            EntityId relationshipId,
            CareRelationshipType relationshipType,
            DateTimeOffset occurredAt) => Successes.Add(relationshipId);

        public void CreationConflict(
            EntityId creatorAccountId,
            EntityId managerProfileId,
            CareRelationshipType relationshipType) => ConflictCount++;

        public void RevocationSucceeded(
            EntityId actorAccountId,
            EntityId managerProfileId,
            EntityId subjectProfileId,
            EntityId relationshipId,
            CareRelationshipType relationshipType,
            DateTimeOffset occurredAt)
        {
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
