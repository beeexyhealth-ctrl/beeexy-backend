namespace Beeexy.Domain.Scheduling;

public sealed record AppointmentRequestFingerprint
{
    public const int Length = 64;

    private AppointmentRequestFingerprint(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static AppointmentRequestFingerprint Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != Length || value.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "The appointment request fingerprint must be a lowercase SHA-256 hexadecimal value.",
                nameof(value));
        }

        return new AppointmentRequestFingerprint(value);
    }

    public override string ToString() => Value;
}
