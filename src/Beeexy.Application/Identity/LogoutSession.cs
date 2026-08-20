using Beeexy.Domain.Common;

namespace Beeexy.Application.Identity;

public sealed class LogoutSession(
    IClock clock,
    ICurrentSessionIdentity currentSessionIdentity,
    IRefreshSessionRepository sessionRepository,
    IIdentityVerificationTransaction transaction,
    IAuthenticationSecurityLogger securityLogger)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var current = currentSessionIdentity.GetRequired();
        var now = clock.UtcNow;

        await transaction.BeginAsync(cancellationToken);
        var session = await sessionRepository.FindByIdForUpdateAsync(
            current.SessionId,
            cancellationToken);
        if (session is null || session.AccountId != current.AccountId)
        {
            throw new SessionAuthenticationException();
        }

        await sessionRepository.RevokeFamilyAsync(
            session.FamilyId,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        securityLogger.SessionFamilyRevoked(
            session.AccountId,
            session.FamilyId,
            "logout");
    }
}
