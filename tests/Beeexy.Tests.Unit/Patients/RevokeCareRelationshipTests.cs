using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Patients;

public sealed class RevokeCareRelationshipTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActiveOwnedRelationship_UsesDomainTransitionAndPersistsMetadata()
    {
        var fixture = new Fixture();

        await fixture.UseCase.ExecuteAsync(fixture.Relationship.Id);

        Assert.Equal(CareRelationshipStatus.Revoked, fixture.Relationship.Status);
        Assert.Equal(Now, fixture.Relationship.RevokedAt);
        Assert.Equal(fixture.Account.Id, fixture.Relationship.RevokedByAccountId);
        Assert.Equal(Now, fixture.Relationship.UpdatedAt);
        Assert.Equal(1, fixture.Transaction.SaveCount);
        Assert.True(fixture.Transaction.Committed);
        var audit = Assert.Single(fixture.Audit.Revocations);
        Assert.Equal(fixture.Account.Id, audit.ActorAccountId);
        Assert.Equal(fixture.PrimaryProfile.Id, audit.ManagerProfileId);
        Assert.Equal(fixture.Subject.Id, audit.SubjectProfileId);
        Assert.Equal(fixture.Relationship.Id, audit.RelationshipId);
        Assert.Equal(Now, audit.OccurredAt);
    }

    [Fact]
    public async Task AlreadyRevokedOwnedRelationship_IsIdempotentAndPreservesOriginalMetadata()
    {
        var fixture = new Fixture();
        var originalTimestamp = Now.AddMinutes(-1);
        fixture.Relationship.Revoke(fixture.Account.Id, originalTimestamp);

        await fixture.UseCase.ExecuteAsync(fixture.Relationship.Id);

        Assert.Equal(originalTimestamp, fixture.Relationship.RevokedAt);
        Assert.Equal(fixture.Account.Id, fixture.Relationship.RevokedByAccountId);
        Assert.Equal(0, fixture.Transaction.SaveCount);
        Assert.True(fixture.Transaction.Committed);
        Assert.Empty(fixture.Audit.Revocations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrForeignRelationship_IsConcealedAsNotFound(bool foreign)
    {
        var fixture = new Fixture();
        fixture.Repository.Relationship = null;
        var requestedId = foreign ? fixture.Relationship.Id : EntityId.New();

        await Assert.ThrowsAsync<CareRelationshipNotFoundException>(() =>
            fixture.UseCase.ExecuteAsync(requestedId));

        Assert.Equal(requestedId, fixture.Repository.RequestedRelationshipId);
        Assert.Equal(fixture.PrimaryProfile.Id, fixture.Repository.RequestedManagerProfileId);
        Assert.False(fixture.Transaction.Committed);
        Assert.Equal(CareRelationshipStatus.Active, fixture.Relationship.Status);
        Assert.Empty(fixture.Audit.Revocations);
    }

    [Fact]
    public async Task DisabledAccount_FailsWithGenericAuthenticationWithoutRevocation()
    {
        var fixture = new Fixture();
        fixture.Account.Disable(Now);

        await Assert.ThrowsAsync<SessionAuthenticationException>(() =>
            fixture.UseCase.ExecuteAsync(fixture.Relationship.Id));

        Assert.True(fixture.Transaction.Began);
        Assert.Equal(0, fixture.Repository.FindCount);
        Assert.Equal(CareRelationshipStatus.Active, fixture.Relationship.Status);
        Assert.Empty(fixture.Audit.Revocations);
    }

    [Fact]
    public async Task MissingPrimaryProfile_FailsInvariantAndDoesNotRevoke()
    {
        var fixture = new Fixture();
        fixture.CurrentRepository.State = fixture.CurrentRepository.State with
        {
            Profiles = Array.Empty<PatientProfile>()
        };

        await Assert.ThrowsAsync<AccountProfileInvariantException>(() =>
            fixture.UseCase.ExecuteAsync(fixture.Relationship.Id));

        Assert.Equal(["primary-profile-count"], fixture.ProfileAudit.InvariantNames);
        Assert.Equal(0, fixture.Repository.FindCount);
        Assert.Equal(CareRelationshipStatus.Active, fixture.Relationship.Status);
    }

    [Fact]
    public async Task OwnershipLookup_UsesManagerProfileRatherThanCreatorIdentity()
    {
        var fixture = new Fixture(creatorAccountId: EntityId.New());

        await fixture.UseCase.ExecuteAsync(fixture.Relationship.Id);

        Assert.Equal(fixture.PrimaryProfile.Id, fixture.Repository.RequestedManagerProfileId);
        Assert.NotEqual(fixture.Account.Id, fixture.Relationship.CreatedByAccountId);
        Assert.Equal(fixture.Account.Id, fixture.Relationship.RevokedByAccountId);
    }

    [Fact]
    public void RepositoryBoundary_CannotDeletePatientsOrRelationships()
    {
        var methods = typeof(ICareRelationshipRevocationRepository).GetMethods();

        var method = Assert.Single(methods);
        Assert.Equal(nameof(ICareRelationshipRevocationRepository.FindForUpdateAsync), method.Name);
        Assert.DoesNotContain(methods, candidate =>
            candidate.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
            candidate.Name.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Fixture
    {
        public Fixture(EntityId? creatorAccountId = null)
        {
            Account = Account.Create(
                NormalizedEmail.Create($"revoke-{Guid.NewGuid():N}@example.com"),
                Now.AddMinutes(-10));
            PrimaryProfile = PatientProfile.Create(
                BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
                Now.AddMinutes(-10),
                Account.Id);
            Subject = PatientProfile.Create(
                BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
                Now.AddMinutes(-5));
            Relationship = CareRelationship.Create(
                PrimaryProfile.Id,
                Subject.Id,
                CareRelationshipType.Caregiver,
                creatorAccountId ?? Account.Id,
                AuthorizationAttestation.Create("phase-3.7-test", Now.AddMinutes(-5)),
                Now.AddMinutes(-5));
            var preference = UserPreference.Create(
                Account.Id,
                UserTimeZone.Create("Etc/UTC"),
                Now.AddMinutes(-10));
            CurrentRepository = new FakeCurrentRepository(new CurrentAccountProfileState(
                Account,
                [PrimaryProfile],
                [preference]));
            ProfileAudit = new FakeProfileAuditLogger();
            Repository = new FakeRevocationRepository(Relationship);
            Transaction = new FakeTransaction();
            Audit = new FakeCareRelationshipAuditLogger();
            var resolver = new CurrentAccountProfileResolver(
                new FakeCurrentSessionIdentity(Account.Id),
                CurrentRepository,
                ProfileAudit);
            UseCase = new RevokeCareRelationship(
                new FakeClock(),
                resolver,
                Repository,
                Transaction,
                Audit);
        }

        public Account Account { get; }

        public PatientProfile PrimaryProfile { get; }

        public PatientProfile Subject { get; }

        public CareRelationship Relationship { get; }

        public FakeCurrentRepository CurrentRepository { get; }

        public FakeProfileAuditLogger ProfileAudit { get; }

        public FakeRevocationRepository Repository { get; }

        public FakeTransaction Transaction { get; }

        public FakeCareRelationshipAuditLogger Audit { get; }

        public RevokeCareRelationship UseCase { get; }
    }

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

    private sealed class FakeRevocationRepository(CareRelationship relationship)
        : ICareRelationshipRevocationRepository
    {
        public CareRelationship? Relationship { get; set; } = relationship;

        public int FindCount { get; private set; }

        public EntityId? RequestedRelationshipId { get; private set; }

        public EntityId? RequestedManagerProfileId { get; private set; }

        public Task<CareRelationship?> FindForUpdateAsync(
            EntityId relationshipId,
            EntityId managerProfileId,
            CancellationToken cancellationToken = default)
        {
            FindCount++;
            RequestedRelationshipId = relationshipId;
            RequestedManagerProfileId = managerProfileId;
            return Task.FromResult(Relationship);
        }
    }

    private sealed class FakeTransaction : IIdentityVerificationTransaction
    {
        public bool Began { get; private set; }

        public bool Committed { get; private set; }

        public int SaveCount { get; private set; }

        public Task BeginAsync(CancellationToken cancellationToken = default)
        {
            Began = true;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

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
        public List<RevocationAudit> Revocations { get; } = [];

        public void CreationSucceeded(
            EntityId creatorAccountId,
            EntityId managerProfileId,
            EntityId subjectProfileId,
            EntityId relationshipId,
            CareRelationshipType relationshipType,
            DateTimeOffset occurredAt)
        {
        }

        public void CreationConflict(
            EntityId creatorAccountId,
            EntityId managerProfileId,
            CareRelationshipType relationshipType)
        {
        }

        public void RevocationSucceeded(
            EntityId actorAccountId,
            EntityId managerProfileId,
            EntityId subjectProfileId,
            EntityId relationshipId,
            CareRelationshipType relationshipType,
            DateTimeOffset occurredAt) => Revocations.Add(new RevocationAudit(
                actorAccountId,
                managerProfileId,
                subjectProfileId,
                relationshipId,
                occurredAt));
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed record RevocationAudit(
        EntityId ActorAccountId,
        EntityId ManagerProfileId,
        EntityId SubjectProfileId,
        EntityId RelationshipId,
        DateTimeOffset OccurredAt);
}
