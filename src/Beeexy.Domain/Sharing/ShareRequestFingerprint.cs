namespace Beeexy.Domain.Sharing;

public sealed record ShareRequestFingerprint
{
    private ShareRequestFingerprint(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ShareRequestFingerprint Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != SharingPersistenceLimits.RequestFingerprint ||
            value.Any(character => character is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "The share request fingerprint must be a lower-case SHA-256 digest.",
                nameof(value));
        }

        return new ShareRequestFingerprint(value);
    }

    public override string ToString() => Value;
}
