namespace Beeexy.Domain.Patients;

public sealed record UserTimeZone
{
    public const int MaximumLength = 100;

    private UserTimeZone(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static UserTimeZone Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var candidate = value.Trim();
        if (candidate.Length > MaximumLength ||
            candidate.Any(char.IsWhiteSpace) ||
            !TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out _))
        {
            throw new ArgumentException("The timezone must be a recognized IANA identifier.", nameof(value));
        }

        return new UserTimeZone(candidate);
    }

    public override string ToString()
    {
        return Value;
    }
}
