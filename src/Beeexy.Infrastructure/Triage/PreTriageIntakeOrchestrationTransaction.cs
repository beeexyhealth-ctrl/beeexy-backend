using Beeexy.Application.Triage;
using Beeexy.Infrastructure.Persistence;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageIntakeOrchestrationTransaction(BeeexyDbContext dbContext)
    : IPreTriageIntakeOrchestrationTransaction
{
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "Pre-triage intake orchestration cannot start inside another transaction.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
