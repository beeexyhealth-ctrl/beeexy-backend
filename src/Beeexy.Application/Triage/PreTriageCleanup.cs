using System.Diagnostics;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Triage;

public sealed class PreTriageCleanupService(
    IClock clock,
    PreTriageCleanupPolicy policy,
    ExpireAnonymousPreTriage expireAnonymous,
    IPreTriageCleanupRepository repository,
    IPreTriageCleanupTelemetry telemetry)
{
    public async Task<PreTriageCleanupResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var cutoff = ToPostgreSqlPrecision(clock.UtcNow);
        var stopwatch = Stopwatch.StartNew();
        var totals = new CleanupTotals(cutoff);
        PreTriageCleanupCursor? cursor = null;

        telemetry.RunStarted(cutoff, policy.BatchSize, policy.MaximumBatchesPerRun);

        for (var batch = 0; batch < policy.MaximumBatchesPerRun; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = await repository.FindCandidatesAsync(
                cutoff,
                policy.BatchSize,
                cursor,
                cancellationToken);
            if (candidates.Count == 0)
            {
                break;
            }

            totals.Batches++;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                totals.Selected++;
                var outcome = candidate.Category == PreTriageCleanupCategory.AuthenticatedAbandoned
                    ? await repository.CleanupLockedAsync(
                        candidate,
                        cutoff,
                        cancellationToken)
                    : await expireAnonymous.ExecuteAsync(
                        candidate,
                        cutoff,
                        cancellationToken);
                totals.Record(candidate.Category, outcome);
            }

            var last = candidates[^1];
            cursor = new PreTriageCleanupCursor(last.EligibleAt, last.SessionId);
            if (candidates.Count < policy.BatchSize)
            {
                break;
            }
        }

        stopwatch.Stop();
        var result = totals.Build(stopwatch.Elapsed);
        telemetry.RunCompleted(result);
        return result;
    }

    private static DateTimeOffset ToPostgreSqlPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);

    private sealed class CleanupTotals(DateTimeOffset cutoff)
    {
        public int Batches { get; set; }

        public int Selected { get; set; }

        public int Removed { get; private set; }

        public int AlreadyAbsent { get; private set; }

        public int SkippedAfterRevalidation { get; private set; }

        public int PreservedPermanent { get; private set; }

        public int AnonymousActiveRemoved { get; private set; }

        public int AnonymousCompletedUnclaimedRemoved { get; private set; }

        public int AuthenticatedAbandonedRemoved { get; private set; }

        public void Record(
            PreTriageCleanupCategory category,
            PreTriageCleanupOutcome outcome)
        {
            switch (outcome)
            {
                case PreTriageCleanupOutcome.Removed:
                    Removed++;
                    if (category == PreTriageCleanupCategory.AnonymousActive)
                    {
                        AnonymousActiveRemoved++;
                    }
                    else if (category ==
                        PreTriageCleanupCategory.AnonymousCompletedUnclaimed)
                    {
                        AnonymousCompletedUnclaimedRemoved++;
                    }
                    else
                    {
                        AuthenticatedAbandonedRemoved++;
                    }

                    break;
                case PreTriageCleanupOutcome.AlreadyAbsent:
                    AlreadyAbsent++;
                    break;
                case PreTriageCleanupOutcome.SkippedAfterRevalidation:
                    SkippedAfterRevalidation++;
                    break;
                case PreTriageCleanupOutcome.PreservedPermanent:
                    PreservedPermanent++;
                    break;
                default:
                    throw new InvalidOperationException("Unknown pre-triage cleanup outcome.");
            }
        }

        public PreTriageCleanupResult Build(TimeSpan duration) => new(
            cutoff,
            Batches,
            Selected,
            Removed,
            AlreadyAbsent,
            SkippedAfterRevalidation,
            PreservedPermanent,
            AnonymousActiveRemoved,
            AnonymousCompletedUnclaimedRemoved,
            AuthenticatedAbandonedRemoved,
            duration);
    }
}

public sealed class ExpireAnonymousPreTriage(IPreTriageCleanupRepository repository)
{
    public Task<PreTriageCleanupOutcome> ExecuteAsync(
        PreTriageCleanupCandidate candidate,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Category == PreTriageCleanupCategory.AuthenticatedAbandoned)
        {
            throw new ArgumentException(
                "The anonymous expiry boundary cannot process authenticated abandonment.",
                nameof(candidate));
        }

        return repository.CleanupLockedAsync(candidate, cutoff, cancellationToken);
    }
}

public sealed class PreTriageCleanupPolicy
{
    public const int MaximumAllowedBatchSize = 1_000;
    public const int MaximumAllowedBatchesPerRun = 1_000;

    public PreTriageCleanupPolicy(int batchSize, int maximumBatchesPerRun)
    {
        if (batchSize is <= 0 or > MaximumAllowedBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        if (maximumBatchesPerRun is <= 0 or > MaximumAllowedBatchesPerRun)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBatchesPerRun));
        }

        BatchSize = batchSize;
        MaximumBatchesPerRun = maximumBatchesPerRun;
    }

    public int BatchSize { get; }

    public int MaximumBatchesPerRun { get; }
}

public enum PreTriageCleanupCategory
{
    AnonymousActive = 0,
    AnonymousCompletedUnclaimed = 1,
    AuthenticatedAbandoned = 2
}

public enum PreTriageCleanupOutcome
{
    Removed = 0,
    AlreadyAbsent = 1,
    SkippedAfterRevalidation = 2,
    PreservedPermanent = 3
}

public sealed record PreTriageCleanupCandidate(
    EntityId SessionId,
    PreTriageCleanupCategory Category,
    DateTimeOffset EligibleAt);

public sealed record PreTriageCleanupCursor(
    DateTimeOffset EligibleAt,
    EntityId SessionId);

public sealed record PreTriageCleanupResult(
    DateTimeOffset Cutoff,
    int Batches,
    int Selected,
    int Removed,
    int AlreadyAbsent,
    int SkippedAfterRevalidation,
    int PreservedPermanent,
    int AnonymousActiveRemoved,
    int AnonymousCompletedUnclaimedRemoved,
    int AuthenticatedAbandonedRemoved,
    TimeSpan Duration);

public interface IPreTriageCleanupRepository
{
    Task<IReadOnlyList<PreTriageCleanupCandidate>> FindCandidatesAsync(
        DateTimeOffset cutoff,
        int batchSize,
        PreTriageCleanupCursor? after,
        CancellationToken cancellationToken = default);

    Task<PreTriageCleanupOutcome> CleanupLockedAsync(
        PreTriageCleanupCandidate candidate,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);
}

public interface IPreTriageCleanupTelemetry
{
    void RunStarted(DateTimeOffset cutoff, int batchSize, int maximumBatches);

    void RunCompleted(PreTriageCleanupResult result);
}
