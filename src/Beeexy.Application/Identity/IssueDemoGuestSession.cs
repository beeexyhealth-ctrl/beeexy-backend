using Beeexy.Domain.Common;

namespace Beeexy.Application.Identity;

public sealed class IssueDemoGuestSession(
    IClock clock,
    IIdentityVerificationTransaction transaction,
    IDemoGuestAccountRepository repository,
    IssueAuthenticationTokens tokenIssuer)
{
    public async Task<IssueDemoGuestSessionResult> ExecuteAsync(
        DemoGuestDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var now = clock.UtcNow;

        await transaction.BeginAsync(cancellationToken);
        var state = await repository.LoadAsync(definition.Email, cancellationToken);
        var resolved = DemoGuestAccountResolver.TryResolve(definition, state);
        if (resolved is null)
        {
            throw new DemoGuestUnavailableException();
        }

        var authenticationSession = tokenIssuer.Execute(resolved.Account.Id, now);
        await transaction.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new IssueDemoGuestSessionResult(
            authenticationSession.Tokens,
            resolved.Account.Id,
            resolved.PrimaryProfile.Id,
            resolved.PrimaryProfile.BeeexyId.Value);
    }
}

public sealed record IssueDemoGuestSessionResult(
    AuthenticationTokenPair Tokens,
    EntityId AccountId,
    EntityId ProfileId,
    string BeeexyId);
