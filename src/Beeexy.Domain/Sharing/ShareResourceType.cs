namespace Beeexy.Domain.Sharing;

public sealed record ShareResourceType
{
    private ShareResourceType(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ShareResourceType Create(string value)
    {
        var normalized = SharingGuard.RequiredText(
            value,
            SharingPersistenceLimits.ResourceType,
            nameof(value));

        if (!IsStableIdentifier(normalized))
        {
            throw new ArgumentException(
                "The resource type must be a lower-case stable identifier.",
                nameof(value));
        }

        return new ShareResourceType(normalized);
    }

    public override string ToString()
    {
        return Value;
    }

    private static bool IsStableIdentifier(string value)
    {
        if (value.Length == 0 || value[0] is < 'a' or > 'z')
        {
            return false;
        }

        return value.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
    }
}
