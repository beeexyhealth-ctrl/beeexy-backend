namespace Beeexy.Domain.Identity;

public sealed record TokenHash
{
    public const int MaximumLength = 512;

    private TokenHash(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static TokenHash FromHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > MaximumLength || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("The token hash representation is invalid.", nameof(value));
        }

        return new TokenHash(value);
    }

    public override string ToString()
    {
        return Value;
    }
}
