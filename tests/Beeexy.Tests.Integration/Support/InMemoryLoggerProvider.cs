using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Beeexy.Tests.Integration.Support;

internal sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages.ToArray();

    public ILogger CreateLogger(string categoryName)
    {
        return new InMemoryLogger(_messages, categoryName);
    }

    public void Dispose()
    {
    }

    private sealed class InMemoryLogger(
        ConcurrentQueue<string> messages,
        string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = $"{categoryName}|{logLevel}|{formatter(state, exception)}";
            if (exception is not null)
            {
                message += $"|{exception}";
            }

            messages.Enqueue(message);
        }
    }
}
