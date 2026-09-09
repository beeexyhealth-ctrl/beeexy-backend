namespace Beeexy.Domain.Sharing;

public static class SharingPersistenceLimits
{
    public const int CapabilityHashMinimum = 32;
    public const int ResourceType = 64;
    public const int MediaType = 255;
    public const int SnapshotVersion = 128;
    public const int ChecksumAlgorithm = 32;
    public const int Checksum = 256;
    public const int PrivateStorageIdentity = 1024;
    public const int FailureCategory = 128;
}
