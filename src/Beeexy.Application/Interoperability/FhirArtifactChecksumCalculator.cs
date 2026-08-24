using System.Security.Cryptography;
using System.Text;

namespace Beeexy.Application.Interoperability;

public sealed class FhirArtifactChecksumCalculator
{
    public const string Algorithm = "SHA-256";

    public string Calculate(ReadOnlySpan<byte> artifactBytes)
    {
        return Convert.ToHexString(SHA256.HashData(artifactBytes))
            .ToLowerInvariant();
    }

    public bool Matches(
        ReadOnlySpan<byte> artifactBytes,
        string checksumAlgorithm,
        string expectedChecksum)
    {
        if (!string.Equals(checksumAlgorithm, Algorithm, StringComparison.Ordinal) ||
            expectedChecksum is null)
        {
            return false;
        }

        var actualBytes = Encoding.ASCII.GetBytes(Calculate(artifactBytes));
        var expectedBytes = Encoding.ASCII.GetBytes(expectedChecksum);
        return actualBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
