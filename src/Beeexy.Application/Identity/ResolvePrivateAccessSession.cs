using Beeexy.Domain.Identity;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Identity;

public sealed class ResolvePrivateAccessSession(
    IClock clock,
    IPrivateAccessTokenService tokenService,
    IPrivateAccessRepository repository,
    IIdentityVerificationTransaction transaction,
    IPrivateAccessAuditLogger auditLogger)
{
    public async Task<ResolvedPrivateAccessSession?> ExecuteAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        TokenHash hash;
        try
        {
            hash = tokenService.Hash(token);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var state = await repository.FindSessionAsync(hash, cancellationToken);
        if (state is null)
        {
            return null;
        }

        var now = clock.UtcNow;
        if (state.Session.Status != PrivateAccessSessionStatus.Active ||
            state.Credential.Status != PrivateAccessCredentialStatus.Active ||
            state.Account?.Status != AccountStatus.Active)
        {
            return null;
        }

        if (!state.Session.IsExpiredAt(now))
        {
            return new ResolvedPrivateAccessSession(
                state.Session.Id,
                state.Credential.Id,
                state.Credential.AccountId,
                state.Session.ExpiresAt);
        }

        await transaction.BeginAsync(cancellationToken);
        var locked = await repository.FindSessionForUpdateAsync(hash, cancellationToken);
        if (locked?.Session is { Status: PrivateAccessSessionStatus.Active } session &&
            session.IsExpiredAt(now))
        {
            session.MarkExpired(now);
            await repository.RevokeRefreshFamilyAsync(
                session.RootRefreshSessionId,
                now,
                cancellationToken);
            await transaction.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            auditLogger.SessionEnded(
                locked.Credential.Id,
                locked.Credential.AccountId,
                "expired");
        }

        return null;
    }
}

public sealed record ResolvedPrivateAccessSession(
    Beeexy.Domain.Common.EntityId SessionId,
    Beeexy.Domain.Common.EntityId CredentialId,
    Beeexy.Domain.Common.EntityId AccountId,
    DateTimeOffset ExpiresAt);
