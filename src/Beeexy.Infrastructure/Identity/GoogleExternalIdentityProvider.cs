using Beeexy.Application.Identity;
using Beeexy.Domain.Identity;

namespace Beeexy.Infrastructure.Identity;

internal sealed class GoogleExternalIdentityProvider(
    GoogleExternalIdentityOptions options,
    IGoogleIdTokenValidator tokenValidator) : IExternalIdentityProvider
{
    public const string ProviderName = "google";

    public string Provider => ProviderName;

    public bool IsEnabled => options.Enabled;

    public async Task<ValidatedExternalIdentity> ValidateAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new ExternalIdentityProviderUnavailableException();
        }

        GoogleIdTokenPayload payload;
        try
        {
            payload = await tokenValidator.ValidateAsync(
                credential,
                options.ClientId,
                cancellationToken);
        }
        catch (GoogleIdTokenRejectedException)
        {
            throw new ExternalIdentityAuthenticationException();
        }
        catch (GoogleIdTokenProviderUnavailableException)
        {
            throw new ExternalIdentityProviderUnavailableException();
        }

        if (string.IsNullOrWhiteSpace(payload.Subject) ||
            payload.Subject.Trim().Length > ExternalIdentity.SubjectMaximumLength)
        {
            throw new ExternalIdentityAuthenticationException();
        }

        NormalizedEmail? verifiedEmail = null;
        if (payload.EmailVerified)
        {
            try
            {
                verifiedEmail = NormalizedEmail.Create(payload.Email ?? string.Empty);
            }
            catch (ArgumentException)
            {
                throw new ExternalIdentityAuthenticationException();
            }
        }

        return new ValidatedExternalIdentity(
            ProviderName,
            payload.Subject.Trim(),
            verifiedEmail);
    }
}
