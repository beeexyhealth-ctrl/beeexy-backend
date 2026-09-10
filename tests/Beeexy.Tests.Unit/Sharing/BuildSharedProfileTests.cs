using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Sharing;

namespace Beeexy.Tests.Unit.Sharing;

public sealed class BuildSharedProfileTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 9, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Phase114")]
    public async Task ActiveMatchingGrant_BuildsCanonicalProfileAndRecordsOneIdempotentEvent()
    {
        var fixture = new Fixture(ShareScope.FullProfile);

        var first = await fixture.UseCase.ExecuteAsync(fixture.Identity);
        var second = await fixture.UseCase.ExecuteAsync(fixture.Identity);

        Assert.Equal(ShareScope.FullProfile, first.Scope);
        Assert.Same(fixture.Snapshot.Profile, first.Profile);
        Assert.Same(fixture.Snapshot.Profile, second.Profile);
        Assert.Equal(2, fixture.Events.Calls.Count);
        Assert.Equal(
            fixture.Events.Calls[0].EventId,
            fixture.Events.Calls[1].EventId);
        Assert.Equal(
            BuildSharedProfile.CreateAccessEventId(
                fixture.Grant.Id,
                fixture.Identity.TokenId),
            fixture.Events.Calls[0].EventId);
    }

    [Theory]
    [InlineData(ShareScope.Case)]
    [InlineData(ShareScope.Visit)]
    [Trait("Category", "Phase114")]
    public async Task ReservedScope_FailsClosedWithoutProjectionOrSuccessEvent(
        ShareScope scope)
    {
        var fixture = new Fixture(scope);

        await Assert.ThrowsAsync<ShareAccessDeniedException>(() =>
            fixture.UseCase.ExecuteAsync(fixture.Identity with { TokenScope = scope }));

        Assert.Equal(0, fixture.Snapshot.Calls);
        Assert.Empty(fixture.Events.Calls);
    }

    [Fact]
    [Trait("Category", "Phase114")]
    public async Task MissingRevokedExpiredAndTokenScopeMismatch_AreIndistinguishablyDenied()
    {
        var missing = new Fixture(ShareScope.FullProfile);
        missing.Repository.State = null;
        await AssertDeniedAsync(missing, missing.Identity);

        var revoked = new Fixture(ShareScope.FullProfile);
        revoked.Grant.Revoke(EntityId.New(), Now.AddMinutes(-1));
        await AssertDeniedAsync(revoked, revoked.Identity);

        var expired = new Fixture(
            ShareScope.FullProfile,
            expiresAt: Now);
        await AssertDeniedAsync(expired, expired.Identity);

        var mismatch = new Fixture(ShareScope.FullProfile);
        await AssertDeniedAsync(
            mismatch,
            mismatch.Identity with { TokenScope = ShareScope.SpecificRecords });
    }

    [Fact]
    [Trait("Category", "Phase114")]
    public void ScopeEvaluator_RequiresExactPreTriageItemsAndSupportedSpecificItems()
    {
        var evaluator = new ShareScopeEvaluator();
        var preTriage = new Fixture(ShareScope.PreTriage);
        var preTriageItem = CreateItem(
            preTriage.Grant,
            SupportedShareResourceTypes.PreTriageEpisode);

        var selection = evaluator.Evaluate(
            preTriage.Grant,
            [preTriageItem],
            ShareScope.PreTriage);

        Assert.False(selection.IncludeFullProfile);
        Assert.Equal(
            SupportedShareResourceTypes.PreTriageEpisode,
            Assert.Single(selection.Items).ResourceType.Value);
        Assert.Throws<ShareAccessDeniedException>(() => evaluator.Evaluate(
            preTriage.Grant,
            [],
            ShareScope.PreTriage));
        Assert.Throws<ShareAccessDeniedException>(() => evaluator.Evaluate(
            preTriage.Grant,
            [CreateItem(preTriage.Grant, SupportedShareResourceTypes.SymptomCheckIn)],
            ShareScope.PreTriage));

        var specific = new Fixture(ShareScope.SpecificRecords);
        var supported = new[]
        {
            SupportedShareResourceTypes.ClinicalHistoryEvent,
            SupportedShareResourceTypes.PreTriageEpisode,
            SupportedShareResourceTypes.SymptomCheckIn,
            SupportedShareResourceTypes.SecondOpinionResult
        }.Select(value => CreateItem(specific.Grant, value)).ToArray();
        Assert.Equal(
            supported.Length,
            evaluator.Evaluate(
                specific.Grant,
                supported,
                ShareScope.SpecificRecords).Items.Count);
        Assert.Throws<ShareAccessDeniedException>(() => evaluator.Evaluate(
            specific.Grant,
            [CreateItem(specific.Grant, "unsupported_record")],
            ShareScope.SpecificRecords));
    }

    [Fact]
    [Trait("Category", "Phase114")]
    public async Task ProjectionFailure_DoesNotRecordMisleadingSuccessEvent()
    {
        var fixture = new Fixture(ShareScope.FullProfile);
        fixture.Snapshot.Fail = true;

        await Assert.ThrowsAsync<ShareAccessDeniedException>(() =>
            fixture.UseCase.ExecuteAsync(fixture.Identity));

        Assert.Empty(fixture.Events.Calls);
    }

    private static async Task AssertDeniedAsync(Fixture fixture, ShareAccessIdentity identity)
    {
        var exception = await Assert.ThrowsAsync<ShareAccessDeniedException>(() =>
            fixture.UseCase.ExecuteAsync(identity));
        Assert.Equal("The share capability is invalid or unavailable.", exception.Message);
        Assert.Empty(fixture.Events.Calls);
    }

    private static ShareGrantItem CreateItem(ShareGrant grant, string resourceType) =>
        ShareGrantItem.Create(
            grant,
            ShareResourceType.Create(resourceType),
            EntityId.New(),
            grant.CreatedAt);

    private sealed class Fixture
    {
        public Fixture(
            ShareScope scope,
            DateTimeOffset? expiresAt = null)
        {
            Grant = ShareGrant.Create(
                EntityId.New(),
                EntityId.New(),
                EntityId.New(),
                ShareRequestFingerprint.Create(new string('a', 64)),
                scope,
                TokenHash.FromHash("sha256:" + new string('b', 64)),
                Now.AddHours(-1),
                expiresAt ?? Now.AddHours(1));
            Repository = new GrantRepository
            {
                State = new SharedProfileGrantState(Grant, [])
            };
            Snapshot = new SnapshotBuilder();
            Events = new EventRecorder();
            UseCase = new BuildSharedProfile(
                Repository,
                new ShareScopeEvaluator(),
                Snapshot,
                Events,
                new FixedClock(Now));
            Identity = new ShareAccessIdentity(Grant.Id, scope, EntityId.New());
        }

        public ShareGrant Grant { get; }
        public GrantRepository Repository { get; }
        public SnapshotBuilder Snapshot { get; }
        public EventRecorder Events { get; }
        public BuildSharedProfile UseCase { get; }
        public ShareAccessIdentity Identity { get; }
    }

    private sealed class GrantRepository : ISharedProfileGrantRepository
    {
        public SharedProfileGrantState? State { get; set; }

        public Task<SharedProfileGrantState?> FindAsync(
            EntityId shareGrantId,
            CancellationToken cancellationToken = default) => Task.FromResult(State);
    }

    private sealed class SnapshotBuilder : ICanonicalSharedHealthSnapshotBuilder
    {
        public CanonicalSharedHealthSnapshot Profile { get; } = new(
            null, [], [], [], [], []);
        public int Calls { get; private set; }
        public bool Fail { get; set; }

        public Task<CanonicalSharedHealthSnapshot> BuildAsync(
            EntityId patientProfileId,
            SharedProfileSelection selection,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fail
                ? Task.FromException<CanonicalSharedHealthSnapshot>(
                    new SharedProfileSourceUnavailableException())
                : Task.FromResult(Profile);
        }
    }

    private sealed class EventRecorder : IShareAccessEventRecorder
    {
        public List<EventCall> Calls { get; } = [];

        public Task RecordSuccessfulAccessAsync(
            ShareGrant grant,
            EntityId eventId,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new EventCall(eventId, occurredAt));
            return Task.CompletedTask;
        }
    }

    private sealed record EventCall(EntityId EventId, DateTimeOffset OccurredAt);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
