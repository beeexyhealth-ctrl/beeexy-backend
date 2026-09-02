using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Infrastructure.Persistence;

namespace Beeexy.Infrastructure.Ai;

internal sealed class AiExecutionRepository(BeeexyDbContext dbContext)
    : IAiExecutionRepository
{
    public void Add(AiExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        dbContext.AiExecutions.Add(execution);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
