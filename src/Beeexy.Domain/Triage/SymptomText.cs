namespace Beeexy.Domain.Triage;

public sealed record SymptomText
{
    public const int MaximumLength = 2000;

    private SymptomText(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static SymptomText Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var candidate = value.Trim();
        if (candidate.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"Symptom text cannot exceed {MaximumLength} characters.",
                nameof(value));
        }

        return new SymptomText(candidate);
    }

    public override string ToString()
    {
        return Value;
    }
}
