using System.Collections.Concurrent;
using Beeexy.Application.Identity;

namespace Beeexy.Infrastructure.Identity;

public sealed class InMemoryAuthenticationEmailSender : IAuthenticationEmailSender
{
    private readonly ConcurrentQueue<AuthenticationEmailMessage> _messages = new();

    public IReadOnlyCollection<AuthenticationEmailMessage> Messages => _messages.ToArray();

    public Task SendAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }
}
