using System.Data;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Beeexy.Infrastructure.Sharing;

internal sealed class ShareRepository(BeeexyDbContext dbContext)
    : IShareCreationTransaction,
      IShareReadRepository,
      IShareExchangeRepository,
      ISharedProfileGrantRepository,
      IShareAccessEventRecorder
{
    private IDbContextTransaction? transaction;

    public async Task BeginAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException("The share transaction is already active.");
        }

        transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var lockKey = $"share-create:{patientProfileId.Value:D}:{idempotencyKey.Value:D}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 112));",
            cancellationToken);
    }

    public async Task<ShareCreationState?> FindExistingAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionActive();
        var grant = await dbContext.ShareGrants
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.PatientProfileId == patientProfileId &&
                    value.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (grant is null)
        {
            return null;
        }

        var itemCount = await dbContext.ShareGrantItems.CountAsync(
            value => value.ShareGrantId == grant.Id,
            cancellationToken);
        return new ShareCreationState(grant, itemCount);
    }

    public async Task<bool> AllItemsBelongToPatientAsync(
        EntityId patientProfileId,
        IReadOnlyList<ShareItemReference> items,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionActive();
        foreach (var group in items.GroupBy(item => item.ResourceType.Value))
        {
            var ids = group.Select(item => item.ResourceId).ToArray();
            var matched = group.Key switch
            {
                SupportedShareResourceTypes.ClinicalHistoryEvent =>
                    await dbContext.ClinicalHistoryEvents.CountAsync(
                        value => ids.Contains(value.Id) &&
                            value.PatientProfileId == patientProfileId,
                        cancellationToken),
                SupportedShareResourceTypes.PreTriageEpisode =>
                    await dbContext.PreTriageEpisodes.CountAsync(
                        value => ids.Contains(value.Id) &&
                            value.PatientProfileId == patientProfileId,
                        cancellationToken),
                SupportedShareResourceTypes.SymptomCheckIn =>
                    await dbContext.SymptomCheckIns.CountAsync(
                        value => ids.Contains(value.Id) &&
                            dbContext.PreTriageEpisodes.Any(episode =>
                                episode.Id == value.EpisodeId &&
                                episode.PatientProfileId == patientProfileId),
                        cancellationToken),
                SupportedShareResourceTypes.SecondOpinionResult =>
                    await dbContext.AiResultSnapshots.CountAsync(
                        value => ids.Contains(value.Id) &&
                            dbContext.AiAnalysisRequests.Any(request =>
                                request.Id == value.AnalysisRequestId &&
                                request.PatientProfileId == patientProfileId &&
                                request.Purpose == AiAnalysisPurpose.SecondOpinion),
                        cancellationToken),
                _ => 0
            };

            if (matched != ids.Length)
            {
                return false;
            }
        }

        return true;
    }

    public void Add(
        ShareGrant grant,
        IReadOnlyList<ShareGrantItem> items,
        ShareAccessEvent createdEvent)
    {
        EnsureTransactionActive();
        dbContext.ShareGrants.Add(grant);
        dbContext.ShareGrantItems.AddRange(items);
        dbContext.ShareAccessEvents.Add(createdEvent);
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        EnsureTransactionActive();
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (transaction is null)
        {
            return;
        }

        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public async Task<IReadOnlyList<ShareCreationState>> ListAsync(
        EntityId patientProfileId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ShareGrants
            .AsNoTracking()
            .Where(grant => grant.PatientProfileId == patientProfileId)
            .OrderByDescending(grant => grant.CreatedAt)
            .ThenByDescending(grant => grant.Id)
            .Select(grant => new ShareCreationState(
                grant,
                dbContext.ShareGrantItems.Count(item => item.ShareGrantId == grant.Id)))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<ShareExchangeState?> FindByCapabilityHashAsync(
        Beeexy.Domain.Identity.TokenHash capabilityHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilityHash);
        return await dbContext.ShareGrants
            .AsNoTracking()
            .Where(grant => grant.CapabilityHash == capabilityHash)
            .Select(grant => new ShareExchangeState(
                grant,
                dbContext.ShareGrantItems.Count(item => item.ShareGrantId == grant.Id)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<SharedProfileGrantState?> FindAsync(
        EntityId shareGrantId,
        CancellationToken cancellationToken = default)
    {
        var grant = await dbContext.ShareGrants
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == shareGrantId, cancellationToken);
        if (grant is null)
        {
            return null;
        }

        var items = await dbContext.ShareGrantItems
            .AsNoTracking()
            .Where(value => value.ShareGrantId == shareGrantId)
            .OrderBy(value => value.ResourceType)
            .ThenBy(value => value.ResourceId)
            .ToArrayAsync(cancellationToken);
        return new SharedProfileGrantState(grant, items);
    }

    public async Task RecordSuccessfulAccessAsync(
        ShareGrant grant,
        EntityId eventId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        var accessEvent = ShareAccessEvent.Create(
            grant,
            ShareAccessEventType.ShareAccessed,
            ShareAccessOutcome.Succeeded,
            occurredAt,
            id: eventId);
        dbContext.ShareAccessEvents.Add(accessEvent);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is Npgsql.PostgresException
            {
                SqlState: Npgsql.PostgresErrorCodes.UniqueViolation,
                ConstraintName: "pk_share_access_events"
            })
        {
            dbContext.Entry(accessEvent).State = EntityState.Detached;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsync();
            transaction = null;
        }
    }

    private void EnsureTransactionActive()
    {
        if (transaction is null)
        {
            throw new InvalidOperationException("A share transaction has not been started.");
        }
    }
}
