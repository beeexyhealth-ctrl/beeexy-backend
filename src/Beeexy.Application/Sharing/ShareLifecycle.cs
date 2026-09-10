using System.Diagnostics;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;

namespace Beeexy.Application.Sharing;

public sealed record ShareActivityItem(
    ShareAccessEventType EventType,
    DateTimeOffset OccurredAt,
    ShareAccessOutcome Outcome,
    ShareResourceType? ResourceCategory);

public sealed record ShareActivityState(
    ShareGrant Grant,
    IReadOnlyList<ShareAccessEvent> Events);

public interface IShareLifecycleTransaction
{
    Task BeginAsync(CancellationToken cancellationToken = default);

    Task<ShareGrant?> FindOwnedForUpdateAsync(
        EntityId shareGrantId,
        EntityId patientProfileId,
        CancellationToken cancellationToken = default);

    Task<bool> EventExistsAsync(
        EntityId shareGrantId,
        ShareAccessEventType eventType,
        CancellationToken cancellationToken = default);

    void AddEvent(ShareAccessEvent accessEvent);

    Task SaveAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public interface IShareActivityRepository
{
    Task<ShareActivityState?> ListAsync(
        EntityId shareGrantId,
        EntityId patientProfileId,
        CancellationToken cancellationToken = default);
}

public sealed record ShareExpiryCandidate(EntityId ShareGrantId, DateTimeOffset ExpiresAt);

public sealed record ShareExpiryCursor(DateTimeOffset ExpiresAt, EntityId ShareGrantId);

public enum ShareExpiryReconciliationOutcome
{
    Reconciled,
    AlreadyReconciled,
    SkippedAfterRevalidation
}

public interface IShareExpiryRepository
{
    Task<IReadOnlyList<ShareExpiryCandidate>> FindCandidatesAsync(
        DateTimeOffset cutoff,
        int batchSize,
        ShareExpiryCursor? after,
        CancellationToken cancellationToken = default);

    Task<ShareExpiryReconciliationOutcome> ReconcileAsync(
        ShareExpiryCandidate candidate,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);
}

public sealed class ShareExpiryPolicy
{
    public const int MaximumAllowedBatchSize = 1_000;
    public const int MaximumAllowedBatchesPerRun = 1_000;

    public ShareExpiryPolicy(int batchSize, int maximumBatchesPerRun)
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

public sealed record ExpireSharesResult(
    DateTimeOffset Cutoff,
    int Batches,
    int Selected,
    int Reconciled,
    int AlreadyReconciled,
    int SkippedAfterRevalidation,
    TimeSpan Duration);

public interface IShareExpiryTelemetry
{
    void RunStarted(DateTimeOffset cutoff, int batchSize, int maximumBatches);

    void RunCompleted(ExpireSharesResult result);
}

public sealed class RevokeShare(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    IShareLifecycleTransaction transaction)
{
    public async Task ExecuteAsync(
        EntityId shareGrantId,
        CancellationToken cancellationToken = default)
    {
        await transaction.BeginAsync(cancellationToken);
        try
        {
            var current = await currentAccountResolver.ResolveAsync(cancellationToken);
            var grant = await transaction.FindOwnedForUpdateAsync(
                shareGrantId,
                current.PrimaryProfile.Id,
                cancellationToken);
            if (grant is null)
            {
                throw new ShareGrantNotFoundException();
            }

            var now = ToPostgreSqlPrecision(clock.UtcNow);
            var changed = false;
            if (now >= grant.ExpiresAt &&
                (!grant.RevokedAt.HasValue || grant.RevokedAt.Value >= grant.ExpiresAt))
            {
                changed |= await AddEventIfMissingAsync(
                    grant,
                    ShareAccessEventType.ShareExpired,
                    grant.ExpiresAt,
                    cancellationToken);
            }

            if (grant.Revoke(current.Account.Id, now))
            {
                changed = true;
                await AddEventIfMissingAsync(
                    grant,
                    ShareAccessEventType.ShareRevoked,
                    now,
                    cancellationToken);
            }

            if (changed)
            {
                await transaction.SaveAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<bool> AddEventIfMissingAsync(
        ShareGrant grant,
        ShareAccessEventType eventType,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (await transaction.EventExistsAsync(
                grant.Id,
                eventType,
                cancellationToken))
        {
            return false;
        }

        transaction.AddEvent(ShareAccessEvent.Create(
            grant,
            eventType,
            ShareAccessOutcome.Succeeded,
            occurredAt,
            id: ShareLifecycleEventIdentity.Create(grant.Id, eventType)));
        return true;
    }

    private static DateTimeOffset ToPostgreSqlPrecision(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}

public sealed class ListShareActivity(
    CurrentAccountProfileResolver currentAccountResolver,
    IShareActivityRepository repository)
{
    public async Task<IReadOnlyList<ShareActivityItem>> ExecuteAsync(
        EntityId shareGrantId,
        CancellationToken cancellationToken = default)
    {
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var state = await repository.ListAsync(
            shareGrantId,
            current.PrimaryProfile.Id,
            cancellationToken);
        if (state is null)
        {
            throw new ShareGrantNotFoundException();
        }

        return state.Events.Select(value => new ShareActivityItem(
            value.EventType,
            value.OccurredAt,
            value.Outcome,
            value.ResourceType)).ToArray();
    }
}

public sealed class ExpireShares(
    IClock clock,
    ShareExpiryPolicy policy,
    IShareExpiryRepository repository,
    IShareExpiryTelemetry telemetry)
{
    public async Task<ExpireSharesResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var cutoff = ToPostgreSqlPrecision(clock.UtcNow);
        var stopwatch = Stopwatch.StartNew();
        var batches = 0;
        var selected = 0;
        var reconciled = 0;
        var alreadyReconciled = 0;
        var skipped = 0;
        ShareExpiryCursor? cursor = null;
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

            batches++;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                selected++;
                var outcome = await repository.ReconcileAsync(
                    candidate,
                    cutoff,
                    cancellationToken);
                switch (outcome)
                {
                    case ShareExpiryReconciliationOutcome.Reconciled:
                        reconciled++;
                        break;
                    case ShareExpiryReconciliationOutcome.AlreadyReconciled:
                        alreadyReconciled++;
                        break;
                    case ShareExpiryReconciliationOutcome.SkippedAfterRevalidation:
                        skipped++;
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unknown share expiry reconciliation outcome.");
                }
            }

            var last = candidates[^1];
            cursor = new ShareExpiryCursor(last.ExpiresAt, last.ShareGrantId);
            if (candidates.Count < policy.BatchSize)
            {
                break;
            }
        }

        stopwatch.Stop();
        var result = new ExpireSharesResult(
            cutoff,
            batches,
            selected,
            reconciled,
            alreadyReconciled,
            skipped,
            stopwatch.Elapsed);
        telemetry.RunCompleted(result);
        return result;
    }

    private static DateTimeOffset ToPostgreSqlPrecision(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}

public static class ShareLifecycleEventIdentity
{
    public static EntityId Create(EntityId grantId, ShareAccessEventType eventType)
    {
        Span<byte> input = stackalloc byte[20];
        grantId.Value.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt32LittleEndian(input[16..], (int)eventType);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        Span<byte> id = stackalloc byte[16];
        hash[..16].CopyTo(id);
        id[7] = (byte)((id[7] & 0x0f) | 0x50);
        id[8] = (byte)((id[8] & 0x3f) | 0x80);
        return EntityId.From(new Guid(id));
    }
}

public sealed class ShareGrantNotFoundException()
    : Exception("The requested share grant could not be found.");
