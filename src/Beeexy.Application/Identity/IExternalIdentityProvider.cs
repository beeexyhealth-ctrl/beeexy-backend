using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public interface IExternalIdentityProvider
{
    string Provider { get; }

    bool IsEnabled { get; }

    Task<ValidatedExternalIdentity> ValidateAsync(
        string credential,
        CancellationToken cancellationToken = default);
}

public sealed record ValidatedExternalIdentity(
    string Provider,
    string Subject,
    NormalizedEmail? VerifiedEmail);
