using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageSessionRepository(BeeexyDbContext dbContext)
    : IPreTriageSessionRepository
{
    public void Add(PreTriageSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        dbContext.PreTriageSessions.Add(session);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
