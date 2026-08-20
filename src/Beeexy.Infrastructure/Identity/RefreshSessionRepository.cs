using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Identity;

public sealed class RefreshSessionRepository(BeeexyDbContext dbContext)
    : IRefreshSessionRepository
{
    public void Add(RefreshSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        dbContext.RefreshSessions.Add(session);
    }

    public async Task<RefreshSession?> FindByTokenHashForUpdateAsync(
        TokenHash tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        var sessions = await dbContext.RefreshSessions
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM identity.refresh_sessions
                WHERE refresh_token_hash = {tokenHash.Value}
                LIMIT 1
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return sessions.SingleOrDefault();
    }

    public async Task<RefreshSession?> FindByIdForUpdateAsync(
        EntityId sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessions = await dbContext.RefreshSessions
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM identity.refresh_sessions
                WHERE id = {sessionId.Value}
                LIMIT 1
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return sessions.SingleOrDefault();
    }

    public Task<Account?> FindAccountAsync(
        EntityId accountId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Accounts.SingleOrDefaultAsync(
            account => account.Id == accountId,
            cancellationToken);
    }

    public Task<PatientProfile?> FindPrimaryProfileAsync(
        EntityId accountId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.PatientProfiles.SingleOrDefaultAsync(
            profile => profile.AccountId == accountId,
            cancellationToken);
    }

    public async Task RevokeFamilyAsync(
        EntityId familyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        await dbContext.RefreshSessions
            .Where(session => session.FamilyId == familyId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.Status, RefreshSessionStatus.Revoked)
                    .SetProperty(session => session.RevokedAt, revokedAt)
                    .SetProperty(session => session.UpdatedAt, revokedAt),
                cancellationToken);
    }
}
