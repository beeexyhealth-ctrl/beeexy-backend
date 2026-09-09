namespace Beeexy.Domain.Sharing;

public sealed record ExportArtifactContentMetadata
{
    private ExportArtifactContentMetadata(
        string checksumAlgorithm,
        string checksum,
        string privateStorageIdentity)
    {
        ChecksumAlgorithm = checksumAlgorithm;
        Checksum = checksum;
        PrivateStorageIdentity = privateStorageIdentity;
    }

    public string ChecksumAlgorithm { get; }

    public string Checksum { get; }

    public string PrivateStorageIdentity { get; }

    public static ExportArtifactContentMetadata Create(
        string checksumAlgorithm,
        string checksum,
        string privateStorageIdentity)
    {
        var normalizedAlgorithm = SharingGuard.RequiredText(
            checksumAlgorithm,
            SharingPersistenceLimits.ChecksumAlgorithm,
            nameof(checksumAlgorithm));
        var normalizedChecksum = SharingGuard.RequiredText(
            checksum,
            SharingPersistenceLimits.Checksum,
            nameof(checksum));
        var normalizedStorageIdentity = SharingGuard.RequiredText(
            privateStorageIdentity,
            SharingPersistenceLimits.PrivateStorageIdentity,
            nameof(privateStorageIdentity));

        if (normalizedAlgorithm.Any(char.IsWhiteSpace) ||
            normalizedChecksum.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Checksum metadata cannot contain whitespace.",
                nameof(checksum));
        }

        return new ExportArtifactContentMetadata(
            normalizedAlgorithm,
            normalizedChecksum,
            normalizedStorageIdentity);
    }
}
