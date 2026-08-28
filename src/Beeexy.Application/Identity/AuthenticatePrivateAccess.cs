using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public sealed class AuthenticatePrivateAccess(
    IClock clock,
    IPrivateAccessSecretHasher secretHasher,
    IPrivateAccessTokenService privateTokenService,
    IPrivateAccessRepository repository,
    IIdentityVerificationTransaction transaction,
    IssueAuthenticationTokens authenticationTokenIssuer,
    IPrivateAccessAuditLogger auditLogger)
{
    public async Task<AuthenticatePrivateAccessResult?> ExecuteAsync(
        AuthenticatePrivateAccessCommand command,
        TimeSpan privateSessionLifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var candidate = await repository.FindCredentialAsync(
            command.Username,
            cancellationToken);

        // Verify both values for both known and unknown usernames. The infrastructure
        // hasher substitutes a fixed valid hash when no stored hash is supplied.
        var passwordMatches = secretHasher.Verify(command.Password, candidate?.PasswordHash);
        var keywordMatches = secretHasher.Verify(command.Keyword, candidate?.KeywordHash);
        if (candidate is null || !(passwordMatches & keywordMatches))
        {
            auditLogger.LoginFailed(
                "credential_mismatch",
                candidate?.Id,
                candidate?.AccountId);
            return null;
        }

        var now = clock.UtcNow;
        await transaction.BeginAsync(cancellationToken);
        var credential = await repository.FindCredentialForUpdateAsync(
            candidate.Id,
            cancellationToken);
        if (credential is not { Status: PrivateAccessCredentialStatus.Active })
        {
            auditLogger.LoginFailed(
                "credential_unavailable",
                candidate.Id,
                candidate.AccountId);
            return null;
        }

        var state = await repository.LoadAccountStateAsync(
            credential.AccountId,
            cancellationToken);
        if (state.Account is not { Status: AccountStatus.Active } account ||
            state.Profiles.Count != 1 ||
            state.Preferences.Count != 1)
        {
            auditLogger.LoginFailed(
                "identity_unavailable",
                credential.Id,
                credential.AccountId);
            return null;
        }

        var authentication = authenticationTokenIssuer.Execute(account.Id, now);
        var privateToken = privateTokenService.Generate();
        var privateSession = PrivateAccessSession.Create(
            credential.Id,
            authentication.Session.FamilyId,
            privateToken.Hash,
            now.Add(privateSessionLifetime),
            now);
        repository.Add(privateSession);
        await transaction.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        auditLogger.LoginSucceeded(credential.Id, account.Id);

        var profile = state.Profiles[0];
        return new AuthenticatePrivateAccessResult(
            privateToken.Value,
            privateSession.ExpiresAt,
            authentication.Tokens,
            account.Id,
            profile.Id,
            profile.BeeexyId.Value);
    }
}

public sealed record AuthenticatePrivateAccessCommand(
    string Username,
    string Password,
    string Keyword);

public sealed record AuthenticatePrivateAccessResult(
    string PrivateToken,
    DateTimeOffset PrivateTokenExpiresAt,
    AuthenticationTokenPair Tokens,
    EntityId AccountId,
    EntityId ProfileId,
    string BeeexyId);
