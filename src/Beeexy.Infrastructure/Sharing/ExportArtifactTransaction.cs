using System.Data;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Beeexy.Infrastructure.Sharing;

internal sealed class ExportArtifactTransaction(BeeexyDbContext dbContext)
    : IExportArtifactTransaction, IAsyncDisposable
{
    private IDbContextTransaction? transaction;

    public async Task BeginAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException("The export transaction is already active.");
        }

        transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var lockKey =
            $"export-create:{patientProfileId.Value:D}:{idempotencyKey.Value:D}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 116));",
            cancellationToken);
    }

    public Task<ExportArtifact?> FindExistingAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureActive();
        return dbContext.ExportArtifacts.SingleOrDefaultAsync(
            value => value.PatientProfileId == patientProfileId &&
                value.IdempotencyKey == idempotencyKey,
            cancellationToken);
    }

    public void Add(ExportArtifact artifact)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(artifact);
        dbContext.ExportArtifacts.Add(artifact);
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        return dbContext.SaveChangesAsync(cancellationToken);
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
            throw new InvalidOperationException("The export transaction is not active.");
        }
    }
}
