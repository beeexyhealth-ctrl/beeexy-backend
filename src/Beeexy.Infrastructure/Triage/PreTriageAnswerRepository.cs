using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageAnswerRepository(BeeexyDbContext dbContext)
    : IPreTriageAnswerRepository
{
    public Task<PreTriageSession?> GetAsync(
        EntityId sessionId,
        CancellationToken cancellationToken = default) =>
        dbContext.PreTriageSessions
            .AsNoTracking()
            .Include(value => value.Answers)
            .SingleOrDefaultAsync(value => value.Id == sessionId, cancellationToken);

    public async Task<TResult?> MutateLockedAsync<TResult>(
        EntityId sessionId,
        Func<PreTriageSession, Task<TResult>> mutation,
        CancellationToken cancellationToken = default)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var session = await dbContext.PreTriageSessions
            .FromSqlInterpolated(
                $"SELECT * FROM triage.pre_triage_sessions WHERE id = {sessionId.Value} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return null;
        }

        await dbContext.Entry(session)
            .Collection(value => value.Answers)
            .LoadAsync(cancellationToken);
        var result = await mutation(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }
}
