namespace Beeexy.Domain.Triage;

public sealed record DefinitionHash
{
    public const int MinimumLength = 32;
    public const int MaximumLength = 128;

    private DefinitionHash(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static DefinitionHash FromHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length is < MinimumLength or > MaximumLength || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("The definition hash representation is invalid.", nameof(value));
        }

        return new DefinitionHash(value);
    }

    public override string ToString()
    {
        return Value;
    }
}
