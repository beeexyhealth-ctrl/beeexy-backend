using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Identity;

public sealed class PrivateAccessRepository(BeeexyDbContext dbContext)
    : IPrivateAccessRepository
{
    public Task<PrivateAccessCredential?> FindCredentialAsync(
        string username,
        CancellationToken cancellationToken = default) =>
        dbContext.PrivateAccessCredentials.SingleOrDefaultAsync(
            value => value.Username == username,
            cancellationToken);

    public async Task<PrivateAccessCredential?> FindCredentialForUpdateAsync(
        EntityId credentialId,
        CancellationToken cancellationToken = default)
    {
        var values = await dbContext.PrivateAccessCredentials
            .FromSqlInterpolated(
                $"SELECT * FROM identity.private_access_credentials WHERE id = {credentialId.Value} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return values.SingleOrDefault();
    }

    public async Task<PrivateAccessAccountState> LoadAccountStateAsync(
        EntityId accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            value => value.Id == accountId,
            cancellationToken);
        var profiles = await dbContext.PatientProfiles
            .Where(value => value.AccountId == accountId)
            .Take(2)
            .ToListAsync(cancellationToken);
        var preferences = await dbContext.UserPreferences
            .Where(value => value.AccountId == accountId)
            .Take(2)
            .ToListAsync(cancellationToken);
        return new PrivateAccessAccountState(account, profiles, preferences);
    }

    public Task<PrivateAccessSessionState?> FindSessionAsync(
        TokenHash tokenHash,
        CancellationToken cancellationToken = default) =>
        QuerySession(tokenHash).SingleOrDefaultAsync(cancellationToken);

    public async Task<PrivateAccessSessionState?> FindSessionForUpdateAsync(
        TokenHash tokenHash,
        CancellationToken cancellationToken = default)
    {
        var sessions = await dbContext.PrivateAccessSessions
            .FromSqlInterpolated(
                $"SELECT * FROM identity.private_access_sessions WHERE token_hash = {tokenHash.Value} FOR UPDATE")
            .ToListAsync(cancellationToken);
        var session = sessions.SingleOrDefault();
        if (session is null)
        {
            return null;
        }

        var credential = await dbContext.PrivateAccessCredentials.SingleAsync(
            value => value.Id == session.CredentialId,
            cancellationToken);
        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            value => value.Id == credential.AccountId,
            cancellationToken);
        return new PrivateAccessSessionState(session, credential, account);
    }

    public void Add(PrivateAccessSession session) => dbContext.PrivateAccessSessions.Add(session);

    public Task RevokeRefreshFamilyAsync(
        EntityId familyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default) =>
        dbContext.RefreshSessions
            .Where(value =>
                value.FamilyId == familyId &&
                value.Status == RefreshSessionStatus.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(value => value.Status, RefreshSessionStatus.Revoked)
                    .SetProperty(value => value.RevokedAt, revokedAt)
                    .SetProperty(value => value.UpdatedAt, revokedAt),
                cancellationToken);

    private IQueryable<PrivateAccessSessionState> QuerySession(TokenHash tokenHash) =>
        from session in dbContext.PrivateAccessSessions.AsNoTracking()
        join credential in dbContext.PrivateAccessCredentials.AsNoTracking()
            on session.CredentialId equals credential.Id
        join accountValue in dbContext.Accounts.AsNoTracking()
            on credential.AccountId equals accountValue.Id into accounts
        from account in accounts.DefaultIfEmpty()
        where session.TokenHash == tokenHash
        select new PrivateAccessSessionState(session, credential, account);
}
