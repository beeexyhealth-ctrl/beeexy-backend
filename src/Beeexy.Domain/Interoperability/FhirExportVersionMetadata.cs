namespace Beeexy.Domain.Interoperability;

public sealed record FhirExportVersionMetadata
{
    public const int MaximumVersionLength = 128;
    public const int MaximumProfileCanonicalLength = 1024;

    private FhirExportVersionMetadata(
        string fhirVersion,
        string mappingVersion,
        string? profileCanonical,
        string? profileVersion)
    {
        FhirVersion = fhirVersion;
        MappingVersion = mappingVersion;
        ProfileCanonical = profileCanonical;
        ProfileVersion = profileVersion;
    }

    public string FhirVersion { get; }

    public string MappingVersion { get; }

    public string? ProfileCanonical { get; }

    public string? ProfileVersion { get; }

    public static FhirExportVersionMetadata Create(
        string fhirVersion,
        string mappingVersion,
        string? profileCanonical = null,
        string? profileVersion = null)
    {
        var normalizedFhirVersion = NormalizeRequired(
            fhirVersion,
            MaximumVersionLength,
            nameof(fhirVersion));
        var normalizedMappingVersion = NormalizeRequired(
            mappingVersion,
            MaximumVersionLength,
            nameof(mappingVersion));
        var normalizedProfileCanonical = NormalizeOptional(
            profileCanonical,
            MaximumProfileCanonicalLength,
            nameof(profileCanonical));
        var normalizedProfileVersion = NormalizeOptional(
            profileVersion,
            MaximumVersionLength,
            nameof(profileVersion));

        if ((normalizedProfileCanonical is null) != (normalizedProfileVersion is null))
        {
            throw new ArgumentException(
                "A profile canonical and profile version must either both be supplied or both be omitted.",
                normalizedProfileCanonical is null
                    ? nameof(profileCanonical)
                    : nameof(profileVersion));
        }

        return new FhirExportVersionMetadata(
            normalizedFhirVersion,
            normalizedMappingVersion,
            normalizedProfileCanonical,
            normalizedProfileVersion);
    }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
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

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        return NormalizeRequired(value, maximumLength, parameterName);
    }
}
