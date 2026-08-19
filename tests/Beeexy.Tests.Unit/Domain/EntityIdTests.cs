using Beeexy.Domain.Common;

namespace Beeexy.Tests.Unit.Domain;

public sealed class EntityIdTests
{
    [Fact]
    public void New_CreatesNonEmptyUniqueIdentifiers()
    {
        var first = EntityId.New();
        var second = EntityId.New();

        Assert.NotEqual(Guid.Empty, first.Value);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void From_PreservesProvidedUuid()
    {
        var value = Guid.NewGuid();

        var entityId = EntityId.From(value);

        Assert.Equal(value, entityId.Value);
        Assert.Equal(value.ToString(), entityId.ToString());
    }

    [Fact]
    public void From_RejectsEmptyUuid()
    {
        Assert.Throws<ArgumentException>(() => EntityId.From(Guid.Empty));
    }
}
