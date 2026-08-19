namespace Beeexy.Domain.Patients;

public sealed record BeeexyId
{
    public const int MaximumLength = 64;

    private BeeexyId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static BeeexyId Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var candidate = value.Trim();
        if (candidate.Length > MaximumLength || candidate.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("The Beeexy identifier is invalid.", nameof(value));
        }

        return new BeeexyId(candidate);
    }

    public override string ToString()
    {
        return Value;
    }
}
