namespace Beeexy.Domain.Directory;

public sealed record IanaTimeZone
{
    public const int MaximumLength = 100;

    private IanaTimeZone(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static IanaTimeZone Create(string value)
    {
        var candidate = DirectoryValueGuard.RequiredIdentifier(
            value,
            MaximumLength,
            nameof(value));
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out _))
        {
            throw new ArgumentException(
                "The timezone must be a recognized IANA identifier.",
                nameof(value));
        }

        return new IanaTimeZone(candidate);
    }

    public override string ToString() => Value;
}
