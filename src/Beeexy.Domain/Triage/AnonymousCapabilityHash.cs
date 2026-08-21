namespace Beeexy.Domain.Triage;

public sealed record AnonymousCapabilityHash
{
    public const int MinimumLength = 32;
    public const int MaximumLength = 128;

    private AnonymousCapabilityHash(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static AnonymousCapabilityHash FromHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length is < MinimumLength or > MaximumLength || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "The anonymous capability hash representation is invalid.",
                nameof(value));
        }

        return new AnonymousCapabilityHash(value);
    }

    public override string ToString()
    {
        return Value;
    }
}
