using Beeexy.Application.Common;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public sealed class AuthenticateWithGoogle(
    IClock clock,
    IExternalIdentityProvider provider,
    IExternalIdentityAuthenticationRepository identityRepository,
    IIdentityVerificationTransaction transaction,
    ProvisionAccountAndPrimaryProfile provisionAccount,
    IssueAuthenticationTokens tokenIssuer)
{
    private const int MaximumCredentialLength = 16_384;

    public async Task<AuthenticateWithGoogleResult> ExecuteAsync(
        AuthenticateWithGoogleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var credential = ValidateCredential(command.Credential);

        if (!provider.IsEnabled)
        {
            throw new ExternalIdentityProviderUnavailableException();
        }

        var validatedIdentity = await provider.ValidateAsync(credential, cancellationToken);
        if (!string.Equals(
                validatedIdentity.Provider,
                provider.Provider,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(validatedIdentity.Subject))
        {
            throw new ExternalIdentityAuthenticationException();
        }

        var now = clock.UtcNow;
        await transaction.BeginAsync(cancellationToken);
        await identityRepository.AcquireIdentityLockAsync(
            provider.Provider,
            validatedIdentity.Subject,
            cancellationToken);

        var existingIdentity = await identityRepository.FindIdentityAsync(
            provider.Provider,
            validatedIdentity.Subject,
            cancellationToken);

        ProvisionedAccountResult resolved;
        if (existingIdentity is not null)
        {
            resolved = await ResolveExistingIdentityAsync(
                existingIdentity,
                validatedIdentity.VerifiedEmail,
                cancellationToken);
        }
        else
        {
            if (validatedIdentity.VerifiedEmail is null)
            {
                throw new ExternalIdentityAuthenticationException();
            }

            try
            {
                resolved = await provisionAccount.ExecuteAsync(
                    validatedIdentity.VerifiedEmail,
                    now,
                    cancellationToken);
            }
            catch (AccountAuthenticationRejectedException)
            {
                throw new ExternalIdentityAuthenticationException();
            }

            identityRepository.Add(ExternalIdentity.Create(
                resolved.Account.Id,
                provider.Provider,
                validatedIdentity.Subject,
                now));
        }

        var authenticationSession = tokenIssuer.Execute(resolved.Account.Id, now);
        await transaction.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AuthenticateWithGoogleResult(
            authenticationSession.Tokens,
            resolved.Account.Id,
            resolved.PrimaryProfile.Id,
            resolved.PrimaryProfile.BeeexyId.Value);
    }

    private async Task<ProvisionedAccountResult> ResolveExistingIdentityAsync(
        ExternalIdentity identity,
        NormalizedEmail? verifiedEmail,
        CancellationToken cancellationToken)
    {
        var account = await identityRepository.FindAccountAsync(
            identity.AccountId,
            cancellationToken);
        if (account is null)
        {
            throw new IdentityProvisioningInvariantException();
        }

        if (account.Status != AccountStatus.Active)
        {
            throw new ExternalIdentityAuthenticationException();
        }

        if (verifiedEmail is not null && account.Email != verifiedEmail)
        {
            var emailAccount = await identityRepository.FindAccountAsync(
                verifiedEmail,
                cancellationToken);
            if (emailAccount is not null && emailAccount.Id != account.Id)
            {
                throw new ExternalIdentityAuthenticationException();
            }
        }

        var profile = await identityRepository.FindPrimaryProfileAsync(
            account.Id,
            cancellationToken);
        if (profile is null)
        {
            throw new IdentityProvisioningInvariantException();
        }

        return new ProvisionedAccountResult(account, profile, false);
    }

    private static string ValidateCredential(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumCredentialLength)
        {
            throw new RequestValidationException(
                "authentication.invalid_google_credential",
                "A Google identity credential is required.");
        }

        return value;
    }
}

public sealed record AuthenticateWithGoogleCommand(string? Credential);

public sealed record AuthenticateWithGoogleResult(
    AuthenticationTokenPair Tokens,
    EntityId AccountId,
    EntityId ProfileId,
    string BeeexyId);
