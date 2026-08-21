namespace Beeexy.Domain.Triage;

public sealed record RuleSetCode
{
    public const int MaximumLength = 100;

    private RuleSetCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static RuleSetCode Create(string value)
    {
        return new RuleSetCode(
            TriageValueGuard.RequiredIdentifier(value, MaximumLength, nameof(value)));
    }

    public override string ToString()
    {
        return Value;
    }
}
