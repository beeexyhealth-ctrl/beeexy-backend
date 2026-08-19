using Beeexy.Domain.Common;

namespace Beeexy.Domain.Identity;

public sealed class ExternalIdentity
{
    public const int ProviderMaximumLength = 50;
    public const int SubjectMaximumLength = 255;

    private ExternalIdentity()
    {
        Provider = null!;
        Subject = null!;
    }

    private ExternalIdentity(
        EntityId id,
        EntityId accountId,
        string provider,
        string subject,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        Provider = provider;
        Subject = subject;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId AccountId { get; private set; }

    public string Provider { get; private set; }

    public string Subject { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static ExternalIdentity Create(
        EntityId accountId,
        string provider,
        string subject,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var normalizedSubject = subject.Trim();

        if (normalizedProvider.Length > ProviderMaximumLength)
        {
            throw new ArgumentException("The external identity provider is too long.", nameof(provider));
        }

        if (normalizedSubject.Length > SubjectMaximumLength)
        {
            throw new ArgumentException("The external identity subject is too long.", nameof(subject));
        }

        return new ExternalIdentity(
            id ?? EntityId.New(),
            accountId,
            normalizedProvider,
            normalizedSubject,
            createdAt);
    }
}
