using System.Data;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Beeexy.Infrastructure.Sharing;

internal sealed class ExportDownloadRepository(BeeexyDbContext dbContext)
    : IExportDownloadRepository
{
    private IDbContextTransaction? transaction;

    public Task<ExportArtifact?> FindArtifactAsync(
        EntityId exportArtifactId,
        CancellationToken cancellationToken = default) =>
        dbContext.ExportArtifacts.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == exportArtifactId,
            cancellationToken);

    public async Task<SharedProfileGrantState?> FindGrantForDownloadAsync(
        EntityId shareGrantId,
        CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException("The export download transaction is active.");
        }

        transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
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

        var grant = await dbContext.ShareGrants.AsNoTracking().SingleAsync(
            value => value.Id == shareGrantId,
            cancellationToken);
        var items = await dbContext.ShareGrantItems.AsNoTracking()
            .Where(value => value.ShareGrantId == shareGrantId)
            .OrderBy(value => value.ResourceType)
            .ThenBy(value => value.ResourceId)
            .ToArrayAsync(cancellationToken);
        return new SharedProfileGrantState(grant, items);
    }

    public async Task RecordSuccessfulDownloadAsync(
        ShareGrant grant,
        EntityId eventId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        EnsureActive();
        if (await dbContext.ShareAccessEvents.AnyAsync(
                value => value.Id == eventId,
                cancellationToken))
        {
            return;
        }

        dbContext.ShareAccessEvents.Add(ShareAccessEvent.Create(
            grant,
            ShareAccessEventType.ShareDownloaded,
            ShareAccessOutcome.Succeeded,
            occurredAt,
            ShareResourceType.Create(SupportedShareResourceTypes.ExportArtifact),
            eventId));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await transaction!.CommitAsync(cancellationToken);
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

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsync();
            transaction = null;
        }
    }

    private void EnsureActive()
    {
        if (transaction is null)
        {
            throw new InvalidOperationException("The export download transaction is not active.");
        }
    }
}
