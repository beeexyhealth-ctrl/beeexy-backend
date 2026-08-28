using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageEducationalVideoOfferRepository(BeeexyDbContext dbContext)
    : IPreTriageEducationalVideoOfferRepository
{
    public async Task<TResult?> MutateLockedAsync<TResult>(
        EntityId sessionId,
        Func<PreTriageSession, Task<TResult>> mutation,
        CancellationToken cancellationToken = default)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
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
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
