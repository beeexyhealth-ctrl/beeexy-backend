namespace Beeexy.Application.Identity;

public interface IAuthenticationEmailSender
{
    Task SendAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken = default);
}
