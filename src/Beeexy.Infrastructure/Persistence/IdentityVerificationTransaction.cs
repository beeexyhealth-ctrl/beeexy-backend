using Beeexy.Application.Identity;
using Microsoft.EntityFrameworkCore.Storage;

namespace Beeexy.Infrastructure.Persistence;

public sealed class IdentityVerificationTransaction(BeeexyDbContext dbContext)
    : IIdentityVerificationTransaction
{
    private IDbContextTransaction? _transaction;

    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("An identity verification transaction is already active.");
        }

        _transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await _transaction!.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.DisposeAsync();
        _transaction = null;
    }

    private void EnsureActive()
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("No identity verification transaction is active.");
        }
    }
}
