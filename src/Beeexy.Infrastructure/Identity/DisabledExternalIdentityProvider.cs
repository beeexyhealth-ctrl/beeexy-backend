using Beeexy.Application.Identity;

namespace Beeexy.Infrastructure.Identity;

internal sealed class DisabledExternalIdentityProvider : IExternalIdentityProvider
{
    public string Provider => GoogleExternalIdentityProvider.ProviderName;

    public bool IsEnabled => false;

    public Task<ValidatedExternalIdentity> ValidateAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        throw new ExternalIdentityProviderUnavailableException();
    }
}
