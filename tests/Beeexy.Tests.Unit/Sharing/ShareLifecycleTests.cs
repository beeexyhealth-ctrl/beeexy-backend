using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Sharing;
using Beeexy.Infrastructure.Sharing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeexy.Tests.Unit.Sharing;

public sealed class ShareLifecycleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 9, 23, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Phase115")]
    public async Task PrimaryRevoke_IsIrreversibleIdempotentAndRecordsOneStableEvent()
    {
        var fixture = new Fixture();

        await fixture.Revoke.ExecuteAsync(fixture.Grant.Id);
        var firstRevokedAt = fixture.Grant.RevokedAt;
        await fixture.Revoke.ExecuteAsync(fixture.Grant.Id);

        Assert.Equal(Now, firstRevokedAt);
        Assert.Equal(firstRevokedAt, fixture.Grant.RevokedAt);
        Assert.Equal(fixture.Account.Id, fixture.Grant.RevokedByAccountId);
        Assert.Equal(2, fixture.Grant.Version);
        var revoked = Assert.Single(fixture.Transaction.Events);
        Assert.Equal(ShareAccessEventType.ShareRevoked, revoked.EventType);
        Assert.Equal(ShareAccessOutcome.Succeeded, revoked.Outcome);
        Assert.Equal(
            ShareLifecycleEventIdentity.Create(
                fixture.Grant.Id,
                ShareAccessEventType.ShareRevoked),
            revoked.Id);
        Assert.Equal(2, fixture.Transaction.CommitCount);
    }

    [Fact]
    [Trait("Category", "Phase115")]
    public async Task MissingOrForeignGrant_IsConcealedAndDoesNotMutateHistory()
    {
        var fixture = new Fixture();
        fixture.Transaction.Owned = false;

        await Assert.ThrowsAsync<ShareGrantNotFoundException>(() =>
            fixture.Revoke.ExecuteAsync(EntityId.New()));

        Assert.Null(fixture.Grant.RevokedAt);
        Assert.Empty(fixture.Transaction.Events);
        Assert.Equal(1, fixture.Transaction.RollbackCount);
    }

    [Fact]
    [Trait("Category", "Phase115")]
    public async Task AlreadyExpiredRevoke_PreservesTruthfulExpiryThenRevocationHistory()
    {
        var fixture = new Fixture(expiresAt: Now.AddMinutes(-5));

        await fixture.Revoke.ExecuteAsync(fixture.Grant.Id);
        await fixture.Revoke.ExecuteAsync(fixture.Grant.Id);

        Assert.Equal(
            [ShareAccessEventType.ShareExpired, ShareAccessEventType.ShareRevoked],
            fixture.Transaction.Events.OrderBy(value => value.OccurredAt)
                .Select(value => value.EventType));
        var expired = fixture.Transaction.Events.Single(value =>
            value.EventType == ShareAccessEventType.ShareExpired);
        Assert.Equal(fixture.Grant.ExpiresAt, expired.OccurredAt);
        Assert.Single(fixture.Transaction.Events, value =>
            value.EventType == ShareAccessEventType.ShareExpired);
        Assert.Single(fixture.Transaction.Events, value =>
            value.EventType == ShareAccessEventType.ShareRevoked);
    }

    [Fact]
    [Trait("Category", "Phase115")]
    public async Task Activity_ProjectsOnlyApprovedSafeFields()
    {
        var fixture = new Fixture();
        var created = ShareAccessEvent.Create(
            fixture.Grant,
            ShareAccessEventType.ShareCreated,
            ShareAccessOutcome.Succeeded,
            fixture.Grant.CreatedAt);
        var accessed = ShareAccessEvent.Create(
            fixture.Grant,
            ShareAccessEventType.ShareAccessed,
            ShareAccessOutcome.Succeeded,
            Now);
        var downloaded = ShareAccessEvent.Create(
            fixture.Grant,
            ShareAccessEventType.ShareDownloaded,
            ShareAccessOutcome.Succeeded,
            Now.AddMinutes(1),
            ShareResourceType.Create("export_artifact"));
        fixture.Activity.State = new ShareActivityState(
            fixture.Grant,
            [created, accessed, downloaded]);

        var result = await fixture.ActivityUseCase.ExecuteAsync(fixture.Grant.Id);

        Assert.Equal(
            [ShareAccessEventType.ShareCreated, ShareAccessEventType.ShareAccessed,
                ShareAccessEventType.ShareDownloaded],
            result.Select(value => value.EventType));
        Assert.Equal("export_artifact", result[^1].ResourceCategory?.Value);
        Assert.All(result, value => Assert.Equal(ShareAccessOutcome.Succeeded, value.Outcome));
        Assert.DoesNotContain(
            typeof(ShareActivityItem).GetProperties(),
            property => new[]
            {
                "Capability", "Hash", "Token", "Ip", "UserAgent", "Account",
                "Storage", "Payload", "Clinical", "Audit"
            }.Any(forbidden => property.Name.Contains(
                forbidden,
                StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    [Trait("Category", "Phase115")]
    public async Task Activity_ForeignGrantIsConcealed()
    {
        var fixture = new Fixture();
        fixture.Activity.State = null;

        await Assert.ThrowsAsync<ShareGrantNotFoundException>(() =>
            fixture.ActivityUseCase.ExecuteAsync(fixture.Grant.Id));
    }

    [Fact]
    [Trait("Category", "Phase115")]
    public async Task Expiry_UsesExactBoundaryBoundedBatchesAndIsRepeatSafe()
    {
        var before = Candidate(Now.AddTicks(-10));
        var exact = Candidate(Now);
        var after = Candidate(Now.AddTicks(10));
        var repository = new ExpiryRepository([before, exact, after]);
        var telemetry = new ExpiryTelemetry();
        var service = new ExpireShares(
            new FixedClock(Now),
            new ShareExpiryPolicy(batchSize: 1, maximumBatchesPerRun: 10),
            repository,
            telemetry);

        var first = await service.ExecuteAsync();
        var second = await service.ExecuteAsync();

        Assert.Equal(2, first.Batches);
        Assert.Equal(2, first.Selected);
        Assert.Equal(2, first.Reconciled);
        Assert.Equal(2, second.AlreadyReconciled);
        Assert.DoesNotContain(repository.Processed, value => value.ShareGrantId == after.ShareGrantId);
        Assert.Equal(2, telemetry.Completed.Count);
    }

    [Fact]
    [Trait("Category", "Phase115")]
    public async Task Expiry_CancellationStopsBeforeSelection()
    {
        var repository = new ExpiryRepository([Candidate(Now)]);
        var service = new ExpireShares(
            new FixedClock(Now),
            new ShareExpiryPolicy(10, 10),
            repository,
            new ExpiryTelemetry());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ExecuteAsync(cancellation.Token));

        Assert.Empty(repository.Processed);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1001, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1001)]
    [Trait("Category", "Phase115")]
    public void ExpiryPolicy_RejectsUnboundedConfiguration(int batchSize, int batches)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ShareExpiryPolicy(batchSize, batches));
    }

    [Fact]
    [Trait("Category", "Phase115")]
    public async Task ExpiryWorker_ContainsFailureAndLeavesRunRetryable()
    {
        var repository = new ExpiryRepository([]) { ThrowOnFind = true };
        var services = new ServiceCollection();
        services.AddSingleton(new ExpireShares(
            new FixedClock(Now),
            new ShareExpiryPolicy(10, 10),
            repository,
            new ExpiryTelemetry()));
        await using var provider = services.BuildServiceProvider();
        var worker = new ShareExpiryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ShareExpiryOptions(TimeSpan.FromMinutes(1), 10, 10),
            NullLogger<ShareExpiryWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Empty(repository.Processed);
    }

    private static ShareExpiryCandidate Candidate(DateTimeOffset expiresAt) =>
        new(EntityId.New(), expiresAt);

    private sealed class Fixture
    {
        public Fixture(DateTimeOffset? expiresAt = null)
        {
            Account = Account.Create(
                NormalizedEmail.Create($"share-lifecycle-{Guid.NewGuid():N}@example.com"),
                Now.AddDays(-1));
            Patient = PatientProfile.Create(
                BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
                Now.AddDays(-1),
                Account.Id);
            var preference = UserPreference.Create(
                Account.Id,
                UserTimeZone.Create("UTC"),
                Now.AddDays(-1));
            var resolver = new CurrentAccountProfileResolver(
                new SessionIdentity(Account.Id),
                new AccountProfileRepository(Account, Patient, preference),
                new NullAuditLogger());
            Grant = ShareGrant.Create(
                Patient.Id,
                Account.Id,
                EntityId.New(),
                ShareRequestFingerprint.Create(new string('a', 64)),
                ShareScope.FullProfile,
                TokenHash.FromHash("sha256:" + new string('b', 64)),
                Now.AddHours(-1),
                expiresAt ?? Now.AddHours(1));
            Transaction = new LifecycleTransaction(Grant);
            Activity = new ActivityRepository();
            Revoke = new RevokeShare(new FixedClock(Now), resolver, Transaction);
            ActivityUseCase = new ListShareActivity(resolver, Activity);
        }

        public Account Account { get; }
        public PatientProfile Patient { get; }
        public ShareGrant Grant { get; }
        public LifecycleTransaction Transaction { get; }
        public ActivityRepository Activity { get; }
        public RevokeShare Revoke { get; }
        public ListShareActivity ActivityUseCase { get; }
    }

    private sealed class LifecycleTransaction(ShareGrant grant) : IShareLifecycleTransaction
    {
        public List<ShareAccessEvent> Events { get; } = [];
        public bool Owned { get; set; } = true;
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }

        public Task BeginAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ShareGrant?> FindOwnedForUpdateAsync(
            EntityId shareGrantId,
            EntityId patientProfileId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            Owned && shareGrantId == grant.Id && patientProfileId == grant.PatientProfileId
                ? grant
                : null);

        public Task<bool> EventExistsAsync(
            EntityId shareGrantId,
            ShareAccessEventType eventType,
            CancellationToken cancellationToken = default) => Task.FromResult(
            Events.Any(value => value.ShareGrantId == shareGrantId &&
                value.EventType == eventType));

        public void AddEvent(ShareAccessEvent accessEvent) => Events.Add(accessEvent);

        public Task SaveAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ActivityRepository : IShareActivityRepository
    {
        public ShareActivityState? State { get; set; }

        public Task<ShareActivityState?> ListAsync(
            EntityId shareGrantId,
            EntityId patientProfileId,
            CancellationToken cancellationToken = default) => Task.FromResult(State);
    }

    private sealed class ExpiryRepository(
        IReadOnlyCollection<ShareExpiryCandidate> candidates) : IShareExpiryRepository
    {
        private readonly HashSet<EntityId> reconciled = [];

        public List<ShareExpiryCandidate> Processed { get; } = [];
        public bool ThrowOnFind { get; set; }

        public Task<IReadOnlyList<ShareExpiryCandidate>> FindCandidatesAsync(
            DateTimeOffset cutoff,
            int batchSize,
            ShareExpiryCursor? after,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnFind)
            {
                throw new InvalidOperationException("sensitive failure detail");
            }

            IReadOnlyList<ShareExpiryCandidate> found = candidates
                .Where(value => value.ExpiresAt <= cutoff)
                .Where(value => after is null || value.ExpiresAt > after.ExpiresAt ||
                    (value.ExpiresAt == after.ExpiresAt &&
                     value.ShareGrantId.Value.CompareTo(after.ShareGrantId.Value) > 0))
                .OrderBy(value => value.ExpiresAt)
                .ThenBy(value => value.ShareGrantId.Value)
                .Take(batchSize)
                .ToArray();
            return Task.FromResult(found);
        }

        public Task<ShareExpiryReconciliationOutcome> ReconcileAsync(
            ShareExpiryCandidate candidate,
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default)
        {
            Processed.Add(candidate);
            return Task.FromResult(reconciled.Add(candidate.ShareGrantId)
                ? ShareExpiryReconciliationOutcome.Reconciled
                : ShareExpiryReconciliationOutcome.AlreadyReconciled);
        }
    }

    private sealed class ExpiryTelemetry : IShareExpiryTelemetry
    {
        public List<ExpireSharesResult> Completed { get; } = [];

        public void RunStarted(DateTimeOffset cutoff, int batchSize, int maximumBatches)
        {
        }

        public void RunCompleted(ExpireSharesResult result) => Completed.Add(result);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class SessionIdentity(EntityId accountId) : ICurrentSessionIdentity
    {
        public CurrentSessionIdentity GetRequired() => new(accountId, EntityId.New());
    }

    private sealed class AccountProfileRepository(
        Account account,
        PatientProfile patient,
        UserPreference preference) : ICurrentAccountProfileRepository
    {
        public Task<CurrentAccountProfileState> LoadAsync(
            EntityId accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CurrentAccountProfileState(account, [patient], [preference]));

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NullAuditLogger : IAccountProfileAuditLogger
    {
        public void InvariantViolation(EntityId accountId, string invariant)
        {
        }

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
}
