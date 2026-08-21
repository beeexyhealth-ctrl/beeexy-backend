namespace Beeexy.Domain.Patients;

public sealed record PatientName
{
    public const int MaximumLength = 100;

    private PatientName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static PatientName Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var candidate = value.Trim();
        if (candidate.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A patient name cannot exceed {MaximumLength} characters.",
                nameof(value));
        }

        return new PatientName(candidate);
    }

    public override string ToString() => Value;
}
