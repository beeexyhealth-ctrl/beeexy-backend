using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Triage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeexy.Tests.Unit.Triage;

public sealed class PreTriageCleanupServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 22, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_UsesExactCutoffAndLeavesFutureCandidateIneligible()
    {
        var repository = new FakeRepository(
        [
            Candidate(-1, PreTriageCleanupCategory.AnonymousActive),
            Candidate(0, PreTriageCleanupCategory.AnonymousCompletedUnclaimed),
            Candidate(1, PreTriageCleanupCategory.AuthenticatedAbandoned)
        ]);
        var service = CreateService(repository, batchSize: 10);

        var result = await service.ExecuteAsync();

        Assert.Equal(Now, result.Cutoff);
        Assert.Equal(2, result.Selected);
        Assert.Equal(2, result.Removed);
        Assert.Equal(1, result.AnonymousActiveRemoved);
        Assert.Equal(1, result.AnonymousCompletedUnclaimedRemoved);
        Assert.Equal(0, result.AuthenticatedAbandonedRemoved);
        Assert.DoesNotContain(repository.Processed, value => value.EligibleAt > Now);
    }

    [Fact]
    public async Task Execute_ProcessesDeterministicBoundedBatchesAndAggregatesOutcomes()
    {
        var candidates = Enumerable.Range(0, 5)
            .Select(index => new PreTriageCleanupCandidate(
                EntityId.From(Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}")),
                (PreTriageCleanupCategory)(index % 3),
                Now.AddMinutes(index - 5)))
            .ToArray();
        var repository = new FakeRepository(candidates)
        {
            Outcomes =
            {
                [candidates[1].SessionId] = PreTriageCleanupOutcome.AlreadyAbsent,
                [candidates[2].SessionId] =
                    PreTriageCleanupOutcome.SkippedAfterRevalidation,
                [candidates[3].SessionId] = PreTriageCleanupOutcome.PreservedPermanent
            }
        };
        var service = CreateService(repository, batchSize: 2);

        var result = await service.ExecuteAsync();

        Assert.Equal(3, result.Batches);
        Assert.Equal(5, result.Selected);
        Assert.Equal(2, result.Removed);
        Assert.Equal(1, result.AlreadyAbsent);
        Assert.Equal(1, result.SkippedAfterRevalidation);
        Assert.Equal(1, result.PreservedPermanent);
        Assert.All(repository.RequestedBatchSizes, value => Assert.Equal(2, value));
        Assert.Equal(candidates, repository.Processed);
    }

    [Fact]
    public async Task RepeatedExecution_TreatsPreviouslyRemovedCandidateAsAbsent()
    {
        var candidate = Candidate(0, PreTriageCleanupCategory.AnonymousActive);
        var repository = new FakeRepository([candidate]) { RetainCandidates = true };
        var service = CreateService(repository, batchSize: 1);

        var first = await service.ExecuteAsync();
        repository.Outcomes[candidate.SessionId] = PreTriageCleanupOutcome.AlreadyAbsent;
        var second = await service.ExecuteAsync();

        Assert.Equal(1, first.Removed);
        Assert.Equal(0, second.Removed);
        Assert.Equal(1, second.AlreadyAbsent);
    }

    [Fact]
    public async Task Cancellation_StopsBeforeCandidateSelection()
    {
        var repository = new FakeRepository(
            [Candidate(0, PreTriageCleanupCategory.AnonymousActive)]);
        var service = CreateService(repository);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ExecuteAsync(cancellation.Token));

        Assert.Empty(repository.Processed);
    }

    [Fact]
    public async Task AnonymousExpiryBoundary_RejectsAuthenticatedCandidate()
    {
        var repository = new FakeRepository([]);
        var useCase = new ExpireAnonymousPreTriage(repository);

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.ExecuteAsync(
            Candidate(0, PreTriageCleanupCategory.AuthenticatedAbandoned),
            Now));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1001, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1001)]
    public void Policy_RejectsUnboundedConfiguration(int batchSize, int maximumBatches)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PreTriageCleanupPolicy(batchSize, maximumBatches));
    }

    [Fact]
    public async Task Worker_ContainsRunFailureSoLaterCadenceCanRetry()
    {
        var repository = new FakeRepository([]) { ThrowOnFind = true };
        var services = new ServiceCollection();
        services.AddSingleton(CreateService(repository));
        await using var provider = services.BuildServiceProvider();
        var worker = new PreTriageCleanupWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new PreTriageCleanupOptions(TimeSpan.FromMinutes(1), 10, 10),
            NullLogger<PreTriageCleanupWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Empty(repository.Processed);
    }

    private static PreTriageCleanupService CreateService(
        FakeRepository repository,
        int batchSize = 10) => new(
        new FixedClock(Now),
        new PreTriageCleanupPolicy(batchSize, 10),
        new ExpireAnonymousPreTriage(repository),
        repository,
        new FakeTelemetry());

    private static PreTriageCleanupCandidate Candidate(
        int offsetMinutes,
        PreTriageCleanupCategory category) => new(
        EntityId.New(),
        category,
        Now.AddMinutes(offsetMinutes));

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeRepository(
        IReadOnlyCollection<PreTriageCleanupCandidate> candidates)
        : IPreTriageCleanupRepository
    {
        public Dictionary<EntityId, PreTriageCleanupOutcome> Outcomes { get; } = [];

        public List<PreTriageCleanupCandidate> Processed { get; } = [];

        public List<int> RequestedBatchSizes { get; } = [];

        public bool RetainCandidates { get; set; }

        public bool ThrowOnFind { get; set; }

        public Task<IReadOnlyList<PreTriageCleanupCandidate>> FindCandidatesAsync(
            DateTimeOffset cutoff,
            int batchSize,
            PreTriageCleanupCursor? after,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnFind)
            {
                throw new InvalidOperationException("sensitive failure detail");
            }

            RequestedBatchSizes.Add(batchSize);
            IReadOnlyList<PreTriageCleanupCandidate> found = candidates
                .Where(value => value.EligibleAt <= cutoff)
                .Where(value => after is null ||
                    value.EligibleAt > after.EligibleAt ||
                    (value.EligibleAt == after.EligibleAt &&
                     value.SessionId.Value.CompareTo(after.SessionId.Value) > 0))
                .OrderBy(value => value.EligibleAt)
                .ThenBy(value => value.SessionId.Value)
                .Take(batchSize)
                .ToArray();
            return Task.FromResult(found);
        }

        public Task<PreTriageCleanupOutcome> CleanupLockedAsync(
            PreTriageCleanupCandidate candidate,
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Processed.Add(candidate);
            var outcome = Outcomes.GetValueOrDefault(
                candidate.SessionId,
                PreTriageCleanupOutcome.Removed);
            if (!RetainCandidates && outcome == PreTriageCleanupOutcome.Removed)
            {
                Outcomes[candidate.SessionId] = PreTriageCleanupOutcome.AlreadyAbsent;
            }

            return Task.FromResult(outcome);
        }
    }

    private sealed class FakeTelemetry : IPreTriageCleanupTelemetry
    {
        public void RunStarted(
            DateTimeOffset cutoff,
            int batchSize,
            int maximumBatches)
        {
        }

        public void RunCompleted(PreTriageCleanupResult result)
        {
        }
    }
}
