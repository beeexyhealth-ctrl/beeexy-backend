namespace Beeexy.Domain.Interoperability;

public sealed record FhirValidatorMetadata
{
    public const int MaximumNameLength = 128;
    public const int MaximumVersionLength = 128;

    private FhirValidatorMetadata(string name, string version)
    {
        Name = name;
        Version = version;
    }

    public string Name { get; }

    public string Version { get; }

    public static FhirValidatorMetadata Create(string name, string version)
    {
        return new FhirValidatorMetadata(
            Normalize(name, MaximumNameLength, nameof(name)),
            Normalize(version, MaximumVersionLength, nameof(version)));
    }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value must contain between 1 and {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }
}
