namespace Beeexy.Domain.Scheduling;

public sealed record AppointmentReason
{
    public const int MaximumLength = 500;

    private AppointmentReason(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static AppointmentReason Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var candidate = value.Trim();
        if (candidate.Length > MaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"The appointment reason cannot exceed {MaximumLength} characters.");
        }

        return new AppointmentReason(candidate);
    }

    public override string ToString() => Value;
}
