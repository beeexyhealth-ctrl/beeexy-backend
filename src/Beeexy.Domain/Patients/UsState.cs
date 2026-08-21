namespace Beeexy.Domain.Patients;

public sealed record UsState
{
    public const int CodeLength = 2;

    private static readonly HashSet<string> ValidCodes =
        new(StringComparer.Ordinal)
        {
            "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA",
            "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD",
            "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ",
            "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC",
            "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY"
        };

    private UsState(string code)
    {
        Code = code;
    }

    public string Code { get; }

    public static UsState Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var candidate = value.Trim().ToUpperInvariant();
        if (!ValidCodes.Contains(candidate))
        {
            throw new ArgumentException(
                "The state must be a valid two-letter U.S. state code.",
                nameof(value));
        }

        return new UsState(candidate);
    }

    public override string ToString() => Code;
}
