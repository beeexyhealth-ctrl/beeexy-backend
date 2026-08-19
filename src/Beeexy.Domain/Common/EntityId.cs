namespace Beeexy.Domain.Common;

public readonly record struct EntityId
{
    private EntityId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static EntityId New()
    {
        return new EntityId(Guid.NewGuid());
    }

    public static EntityId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", nameof(value));
        }

        return new EntityId(value);
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
