using Beeexy.Domain.Common;

namespace Beeexy.Tests.Unit.Domain;

public sealed class AuditMetadataTests
{
    [Fact]
    public void Create_UsesUnambiguousClockInstant()
    {
        var now = new DateTimeOffset(2026, 8, 19, 20, 0, 0, TimeSpan.Zero);

        var metadata = AuditMetadata.Create(new StubClock(now));

        Assert.Equal(now, metadata.CreatedAt);
        Assert.Null(metadata.LastModifiedAt);
    }

    [Fact]
    public void Touch_ReturnsNewMetadataAndPreservesCreationInstant()
    {
        var createdAt = new DateTimeOffset(2026, 8, 19, 20, 0, 0, TimeSpan.Zero);
        var modifiedAt = createdAt.AddMinutes(5);
        var original = AuditMetadata.Create(new StubClock(createdAt));

        var updated = original.Touch(new StubClock(modifiedAt));

        Assert.Equal(createdAt, updated.CreatedAt);
        Assert.Equal(modifiedAt, updated.LastModifiedAt);
        Assert.Null(original.LastModifiedAt);
    }

    [Fact]
    public void Touch_RejectsInstantBeforeCreation()
    {
        var createdAt = new DateTimeOffset(2026, 8, 19, 20, 0, 0, TimeSpan.Zero);
        var metadata = AuditMetadata.Create(new StubClock(createdAt));

        Assert.Throws<InvalidOperationException>(() =>
            metadata.Touch(new StubClock(createdAt.AddSeconds(-1))));
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
