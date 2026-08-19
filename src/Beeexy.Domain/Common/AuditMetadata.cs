namespace Beeexy.Domain.Common;

public sealed record AuditMetadata
{
    private AuditMetadata(DateTimeOffset createdAt, DateTimeOffset? lastModifiedAt)
    {
        CreatedAt = createdAt;
        LastModifiedAt = lastModifiedAt;
    }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? LastModifiedAt { get; private init; }

    public static AuditMetadata Create(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new AuditMetadata(clock.UtcNow, null);
    }

    public AuditMetadata Touch(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var modifiedAt = clock.UtcNow;
        if (modifiedAt < CreatedAt)
        {
            throw new InvalidOperationException(
                "Audit modification time cannot precede creation time.");
        }

        return this with { LastModifiedAt = modifiedAt };
    }
}
