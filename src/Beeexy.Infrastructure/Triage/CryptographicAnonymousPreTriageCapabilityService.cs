using System.Security.Cryptography;
using System.Text;
using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;

namespace Beeexy.Infrastructure.Triage;

public sealed class CryptographicAnonymousPreTriageCapabilityService
    : IAnonymousPreTriageCapabilityService
{
    private const string CapabilityPrefix = "ptc1.";
    private const string HashPrefix = "sha256:";
    private const int CapabilityByteLength = 32;
    private const int EncodedCapabilityLength = 43;
    private static readonly string DummyCapability =
        CapabilityPrefix + new string('A', EncodedCapabilityLength);

    public GeneratedAnonymousCapability Generate()
    {
        var capability = CapabilityPrefix + Base64UrlEncode(
            RandomNumberGenerator.GetBytes(CapabilityByteLength));
        return new GeneratedAnonymousCapability(capability, Hash(capability));
    }

    public AnonymousCapabilityHash Hash(string capability)
    {
        if (!HasValidFormat(capability))
        {
            throw new ArgumentException(
                "The anonymous pre-triage capability is malformed.",
                nameof(capability));
        }

        return AnonymousCapabilityHash.FromHash(HashRepresentation(capability));
    }

    public bool Verify(string? capability, AnonymousCapabilityHash expectedHash)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);

        var validCandidate = HasValidFormat(capability);
        var candidateDigest = SHA256.HashData(
            Encoding.UTF8.GetBytes(validCandidate ? capability! : DummyCapability));
        var expectedDigest = ParseExpectedDigest(expectedHash, out var validExpectedHash);
        var hashesMatch = CryptographicOperations.FixedTimeEquals(
            candidateDigest,
            expectedDigest);
        return validCandidate & validExpectedHash & hashesMatch;
    }

    private static bool HasValidFormat(string? capability)
    {
        if (capability is null ||
            capability.Length != CapabilityPrefix.Length + EncodedCapabilityLength ||
            !capability.StartsWith(CapabilityPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return capability.AsSpan(CapabilityPrefix.Length).IndexOfAnyExcept(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".AsSpan()) < 0;
    }

    private static string HashRepresentation(string capability)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(capability));
        return HashPrefix + Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static byte[] ParseExpectedDigest(
        AnonymousCapabilityHash expectedHash,
        out bool isValid)
    {
        var value = expectedHash.Value;
        if (value.Length == HashPrefix.Length + (SHA256.HashSizeInBytes * 2) &&
            value.StartsWith(HashPrefix, StringComparison.Ordinal))
        {
            try
            {
                var result = Convert.FromHexString(value[HashPrefix.Length..]);
                isValid = result.Length == SHA256.HashSizeInBytes;
                return result;
            }
            catch (FormatException)
            {
                // A malformed persisted representation is treated as a failed verification.
            }
        }

        isValid = false;
        return new byte[SHA256.HashSizeInBytes];
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
