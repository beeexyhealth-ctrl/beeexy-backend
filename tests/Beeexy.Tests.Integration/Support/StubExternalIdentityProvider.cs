using System.Collections.Concurrent;
using Beeexy.Application.Identity;
using Beeexy.Domain.Identity;

namespace Beeexy.Tests.Integration.Support;

internal sealed class StubExternalIdentityProvider : IExternalIdentityProvider
{
    private readonly ConcurrentDictionary<string, Func<ValidatedExternalIdentity>> _responses =
        new(StringComparer.Ordinal);

    public string Provider => "google";

    public bool IsEnabled => true;

    public void Accept(
        string credential,
        string subject,
        string? email,
        bool emailVerified = true)
    {
        _responses[credential] = () => new ValidatedExternalIdentity(
            Provider,
            subject,
            emailVerified && email is not null ? NormalizedEmail.Create(email) : null);
    }

    public void Reject(string credential)
    {
        _responses[credential] = () => throw new ExternalIdentityAuthenticationException();
    }

    public void MakeUnavailable(string credential)
    {
        _responses[credential] = () => throw new ExternalIdentityProviderUnavailableException();
    }

    public Task<ValidatedExternalIdentity> ValidateAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_responses.TryGetValue(credential, out var response))
        {
            throw new ExternalIdentityAuthenticationException();
        }

        return Task.FromResult(response());
    }
}
