using Beeexy.Application.Identity;

namespace Beeexy.Infrastructure.Identity;

internal sealed class UnavailableAuthenticationEmailSender : IAuthenticationEmailSender
{
    public Task SendAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        throw new AuthenticationEmailDeliveryException();
    }
}
