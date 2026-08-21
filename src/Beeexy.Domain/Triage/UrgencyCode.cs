namespace Beeexy.Domain.Triage;

public sealed record UrgencyCode
{
    public const int MaximumLength = 100;

    private UrgencyCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static UrgencyCode Create(string value)
    {
        return new UrgencyCode(
            TriageValueGuard.RequiredIdentifier(value, MaximumLength, nameof(value)));
    }

    public override string ToString()
    {
        return Value;
    }
}
