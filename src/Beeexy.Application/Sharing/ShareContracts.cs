using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Sharing;

namespace Beeexy.Application.Sharing;

public sealed record ShareItemReference(
    ShareResourceType ResourceType,
    EntityId ResourceId);

public sealed record CreateShareCommand(
    ShareScope Scope,
    int? LifetimeMinutes,
    EntityId IdempotencyKey,
    IReadOnlyList<ShareItemReference> Items);

public sealed record ShareSummary(
    EntityId ShareGrantId,
    ShareScope Scope,
    ShareGrantStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    int ItemCount);

public sealed record CreateShareResult(
    ShareSummary Share,
    bool NewlyCreated,
    string? Capability,
    string? ShareUrl);

public sealed record ShareCreationState(ShareGrant Grant, int ItemCount);

public interface IShareCreationTransaction : IAsyncDisposable
{
    Task BeginAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<ShareCreationState?> FindExistingAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<bool> AllItemsBelongToPatientAsync(
        EntityId patientProfileId,
        IReadOnlyList<ShareItemReference> items,
        CancellationToken cancellationToken = default);

    void Add(
        ShareGrant grant,
        IReadOnlyList<ShareGrantItem> items,
        ShareAccessEvent createdEvent);

    Task SaveAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IShareReadRepository
{
    Task<IReadOnlyList<ShareCreationState>> ListAsync(
        EntityId patientProfileId,
        CancellationToken cancellationToken = default);
}

public interface IShareCapabilityService
{
    GeneratedShareCapability Generate();

    TokenHash Hash(string capability);
}

public sealed class GeneratedShareCapability
{
    public GeneratedShareCapability(string value, TokenHash hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentNullException.ThrowIfNull(hash);
        Value = value;
        Hash = hash;
    }

    public string Value { get; }

    public TokenHash Hash { get; }

    public override string ToString() => "[REDACTED]";
}

public sealed class ShareIdempotencyConflictException : Exception
{
    public ShareIdempotencyConflictException()
        : base("The idempotency key was already used for a different share request.")
    {
    }
}

public sealed class ShareItemNotFoundException : Exception
{
    public ShareItemNotFoundException()
        : base("A requested share item was not found for the current patient.")
    {
    }
}
