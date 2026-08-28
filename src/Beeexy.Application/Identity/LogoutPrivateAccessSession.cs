using Beeexy.Domain.Identity;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Identity;

public sealed class LogoutPrivateAccessSession(
    IClock clock,
    IPrivateAccessTokenService tokenService,
    IPrivateAccessRepository repository,
    IIdentityVerificationTransaction transaction,
    IPrivateAccessAuditLogger auditLogger)
{
    public async Task ExecuteAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        TokenHash hash;
        try
        {
            hash = tokenService.Hash(token);
        }
        catch (ArgumentException)
        {
            return;
        }

        await transaction.BeginAsync(cancellationToken);
        var state = await repository.FindSessionForUpdateAsync(hash, cancellationToken);
        if (state?.Session is not { Status: PrivateAccessSessionStatus.Active } session)
        {
            return;
        }

        var now = clock.UtcNow;
        session.Revoke(now);
        await repository.RevokeRefreshFamilyAsync(
            session.RootRefreshSessionId,
            now,
            cancellationToken);
        await transaction.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        auditLogger.SessionEnded(state.Credential.Id, state.Credential.AccountId, "logout");
    }
}
