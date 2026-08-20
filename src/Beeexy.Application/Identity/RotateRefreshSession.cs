using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public sealed class RotateRefreshSession(
    IClock clock,
    IRefreshTokenService refreshTokenService,
    IRefreshSessionRepository sessionRepository,
    IIdentityVerificationTransaction transaction,
    IssueAuthenticationTokens tokenIssuer,
    IAuthenticationSecurityLogger securityLogger)
{
    public async Task<RotateRefreshSessionResult> ExecuteAsync(
        RotateRefreshSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TokenHash tokenHash;
        try
        {
            tokenHash = refreshTokenService.Hash(command.RefreshToken ?? string.Empty);
        }
        catch (ArgumentException)
        {
            throw new SessionAuthenticationException();
        }

        var now = clock.UtcNow;
        await transaction.BeginAsync(cancellationToken);
        var session = await sessionRepository.FindByTokenHashForUpdateAsync(
            tokenHash,
            cancellationToken);
        if (session is null)
        {
            throw new SessionAuthenticationException();
        }

        if (session.Status == RefreshSessionStatus.Revoked)
        {
            if (session.ReplacedBySessionId.HasValue)
            {
                await sessionRepository.RevokeFamilyAsync(
                    session.FamilyId,
                    now,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                securityLogger.RefreshReuseDetected(session.AccountId, session.FamilyId);
            }

            throw new SessionAuthenticationException();
        }

        if (session.Status == RefreshSessionStatus.Expired || session.IsExpiredAt(now))
        {
            if (session.Status == RefreshSessionStatus.Active)
            {
                session.MarkExpired(now);
                await transaction.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            throw new SessionAuthenticationException();
        }

        var account = await sessionRepository.FindAccountAsync(
            session.AccountId,
            cancellationToken);
        if (account is null || account.Status != AccountStatus.Active)
        {
            await sessionRepository.RevokeFamilyAsync(
                session.FamilyId,
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            securityLogger.SessionFamilyRevoked(
                session.AccountId,
                session.FamilyId,
                "account_unavailable");
            throw new SessionAuthenticationException();
        }

        var profile = await sessionRepository.FindPrimaryProfileAsync(
            account.Id,
            cancellationToken);
        if (profile is null)
        {
            throw new IdentityProvisioningInvariantException();
        }

        var successor = tokenIssuer.Execute(
            account.Id,
            now,
            session.FamilyId,
            session.Id);
        session.Rotate(successor.Session.Id, now);
        await transaction.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        securityLogger.RefreshSessionRotated(account.Id, session.FamilyId);

        return new RotateRefreshSessionResult(
            successor.Tokens,
            account.Id,
            profile.Id,
            profile.BeeexyId.Value);
    }
}

public sealed record RotateRefreshSessionCommand(string? RefreshToken);

public sealed record RotateRefreshSessionResult(
    AuthenticationTokenPair Tokens,
    EntityId AccountId,
    EntityId ProfileId,
    string BeeexyId);
