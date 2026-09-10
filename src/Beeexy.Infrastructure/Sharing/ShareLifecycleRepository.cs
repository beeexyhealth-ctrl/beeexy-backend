using System.Data;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Beeexy.Infrastructure.Sharing;

internal sealed class ShareLifecycleRepository(BeeexyDbContext dbContext)
    : IShareLifecycleTransaction,
      IShareActivityRepository,
      IShareExpiryRepository,
      IAsyncDisposable
{
    private IDbContextTransaction? transaction;

    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException("The share lifecycle transaction is active.");
        }

        transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
    }

    public async Task<ShareGrant?> FindOwnedForUpdateAsync(
        EntityId shareGrantId,
        EntityId patientProfileId,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionActive();
        if (!await LockGrantAsync(
                shareGrantId.Value,
                patientProfileId.Value,
                cancellationToken))
        {
            return null;
        }

        return await dbContext.ShareGrants.SingleAsync(
            value => value.Id == shareGrantId,
            cancellationToken);
    }

    public Task<bool> EventExistsAsync(
        EntityId shareGrantId,
        ShareAccessEventType eventType,
        CancellationToken cancellationToken = default) =>
        dbContext.ShareAccessEvents.AnyAsync(
            value => value.ShareGrantId == shareGrantId && value.EventType == eventType,
            cancellationToken);

    public void AddEvent(ShareAccessEvent accessEvent)
    {
        EnsureTransactionActive();
        dbContext.ShareAccessEvents.Add(accessEvent);
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

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (transaction is null)
        {
            return;
        }

        await transaction.RollbackAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public async Task<ShareActivityState?> ListAsync(
        EntityId shareGrantId,
        EntityId patientProfileId,
        CancellationToken cancellationToken = default)
    {
        var grant = await dbContext.ShareGrants
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == shareGrantId &&
                    value.PatientProfileId == patientProfileId,
                cancellationToken);
        if (grant is null)
        {
            return null;
        }

        var events = await dbContext.ShareAccessEvents
            .AsNoTracking()
            .Where(value => value.ShareGrantId == shareGrantId)
            .OrderBy(value => value.OccurredAt)
            .ThenBy(value => value.Id)
            .ToArrayAsync(cancellationToken);
        return new ShareActivityState(grant, events);
    }

    public async Task<IReadOnlyList<ShareExpiryCandidate>> FindCandidatesAsync(
        DateTimeOffset cutoff,
        int batchSize,
        ShareExpiryCursor? after,
        CancellationToken cancellationToken = default)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT share_grant.id, share_grant.expires_at
                FROM sharing.share_grants AS share_grant
                WHERE share_grant.expires_at <= @cutoff
                  AND share_grant.revoked_at IS NULL
                  AND NOT EXISTS (
                    SELECT 1
                    FROM sharing.share_access_events AS access_event
                    WHERE access_event.share_grant_id = share_grant.id
                      AND access_event.event_type = 'share_expired'
                  )
                  AND (
                    @after_expires_at IS NULL
                    OR share_grant.expires_at > @after_expires_at
                    OR (share_grant.expires_at = @after_expires_at
                        AND share_grant.id > @after_grant_id)
                  )
                ORDER BY share_grant.expires_at, share_grant.id
                LIMIT @batch_size;
                """;
            command.Parameters.Add(new NpgsqlParameter(
                "cutoff",
                NpgsqlDbType.TimestampTz)
            {
                Value = cutoff
            });
            command.Parameters.Add(new NpgsqlParameter(
                "after_expires_at",
                NpgsqlDbType.TimestampTz)
            {
                Value = after is null ? DBNull.Value : after.ExpiresAt
            });
            command.Parameters.Add(new NpgsqlParameter(
                "after_grant_id",
                NpgsqlDbType.Uuid)
            {
                Value = after is null ? DBNull.Value : after.ShareGrantId.Value
            });
            command.Parameters.Add(new NpgsqlParameter(
                "batch_size",
                NpgsqlDbType.Integer)
            {
                Value = batchSize
            });

            var candidates = new List<ShareExpiryCandidate>(batchSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new ShareExpiryCandidate(
                    EntityId.From(reader.GetGuid(0)),
                    reader.GetFieldValue<DateTimeOffset>(1)));
            }

            return candidates;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<ShareExpiryReconciliationOutcome> ReconcileAsync(
        ShareExpiryCandidate candidate,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        await BeginAsync(cancellationToken);
        try
        {
            var grant = await FindForUpdateAsync(candidate.ShareGrantId, cancellationToken);
            if (grant is null || grant.RevokedAt.HasValue || grant.ExpiresAt > cutoff)
            {
                await CommitAsync(cancellationToken);
                return ShareExpiryReconciliationOutcome.SkippedAfterRevalidation;
            }

            if (await EventExistsAsync(
                    grant.Id,
                    ShareAccessEventType.ShareExpired,
                    cancellationToken))
            {
                await CommitAsync(cancellationToken);
                return ShareExpiryReconciliationOutcome.AlreadyReconciled;
            }

            AddEvent(ShareAccessEvent.Create(
                grant,
                ShareAccessEventType.ShareExpired,
                ShareAccessOutcome.Succeeded,
                grant.ExpiresAt,
                id: ShareLifecycleEventIdentity.Create(
                    grant.Id,
                    ShareAccessEventType.ShareExpired)));
            await SaveAsync(cancellationToken);
            await CommitAsync(cancellationToken);
            return ShareExpiryReconciliationOutcome.Reconciled;
        }
        catch
        {
            await RollbackAsync(CancellationToken.None);
            throw;
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

    private async Task<ShareGrant?> FindForUpdateAsync(
        EntityId shareGrantId,
        CancellationToken cancellationToken)
    {
        EnsureTransactionActive();
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction!.GetDbTransaction();
        command.CommandText =
            "SELECT id FROM sharing.share_grants WHERE id = @shareGrantId FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "shareGrantId";
        parameter.Value = shareGrantId.Value;
        command.Parameters.Add(parameter);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            return null;
        }

        return await dbContext.ShareGrants.SingleAsync(
            value => value.Id == shareGrantId,
            cancellationToken);
    }

    private async Task<bool> LockGrantAsync(
        Guid shareGrantId,
        Guid patientProfileId,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction!.GetDbTransaction();
        command.CommandText =
            "SELECT id FROM sharing.share_grants " +
            "WHERE id = @shareGrantId AND patient_profile_id = @patientProfileId FOR UPDATE";
        var grantParameter = command.CreateParameter();
        grantParameter.ParameterName = "shareGrantId";
        grantParameter.Value = shareGrantId;
        command.Parameters.Add(grantParameter);
        var patientParameter = command.CreateParameter();
        patientParameter.ParameterName = "patientProfileId";
        patientParameter.Value = patientProfileId;
        command.Parameters.Add(patientParameter);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private void EnsureTransactionActive()
    {
        if (transaction is null)
        {
            throw new InvalidOperationException("A share lifecycle transaction is not active.");
        }
    }
}
