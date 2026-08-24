using System.Security.Cryptography;

namespace Beeexy.Application.Interoperability;

public sealed class FhirArtifactChecksumCalculator
{
    public const string Algorithm = "SHA-256";

    public string Calculate(ReadOnlySpan<byte> artifactBytes)
    {
        return Convert.ToHexString(SHA256.HashData(artifactBytes))
            .ToLowerInvariant();
    }
}
