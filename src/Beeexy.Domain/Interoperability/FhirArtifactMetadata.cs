namespace Beeexy.Domain.Interoperability;

public sealed record FhirArtifactMetadata
{
    public const int MaximumChecksumAlgorithmLength = 32;
    public const int MaximumChecksumLength = 256;
    public const int MaximumStorageUriLength = 2048;

    private FhirArtifactMetadata(
        string checksumAlgorithm,
        string checksum,
        string privateStorageUri)
    {
        ChecksumAlgorithm = checksumAlgorithm;
        Checksum = checksum;
        PrivateStorageUri = privateStorageUri;
    }

    public string ChecksumAlgorithm { get; }

    public string Checksum { get; }

    public string PrivateStorageUri { get; }

    public static FhirArtifactMetadata Create(
        string checksumAlgorithm,
        string checksum,
        string privateStorageUri)
    {
        var normalizedAlgorithm = Normalize(
            checksumAlgorithm,
            MaximumChecksumAlgorithmLength,
            nameof(checksumAlgorithm));
        var normalizedChecksum = Normalize(
            checksum,
            MaximumChecksumLength,
            nameof(checksum));
        var normalizedUri = Normalize(
            privateStorageUri,
            MaximumStorageUriLength,
            nameof(privateStorageUri));

        if (!Uri.TryCreate(normalizedUri, UriKind.Absolute, out var parsedUri))
        {
            throw new ArgumentException(
                "The private artifact storage URI must be absolute.",
                nameof(privateStorageUri));
        }

        if (!string.IsNullOrEmpty(parsedUri.UserInfo))
        {
            throw new ArgumentException(
                "The private artifact storage URI cannot contain credentials.",
                nameof(privateStorageUri));
        }

        return new FhirArtifactMetadata(
            normalizedAlgorithm,
            normalizedChecksum,
            normalizedUri);
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
