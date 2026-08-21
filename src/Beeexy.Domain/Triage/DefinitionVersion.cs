namespace Beeexy.Domain.Triage;

public sealed record DefinitionVersion
{
    public const int MaximumLength = 64;

    private DefinitionVersion(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static DefinitionVersion Create(string value)
    {
        return new DefinitionVersion(
            TriageValueGuard.RequiredIdentifier(value, MaximumLength, nameof(value)));
    }

    public override string ToString()
    {
        return Value;
    }
}
