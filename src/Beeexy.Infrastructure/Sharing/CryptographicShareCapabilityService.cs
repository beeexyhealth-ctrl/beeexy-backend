using System.Security.Cryptography;
using System.Text;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Identity;

namespace Beeexy.Infrastructure.Sharing;

public sealed class CryptographicShareCapabilityService : IShareCapabilityService
{
    public const string CapabilityPrefix = "shc1.";
    public const int CapabilityByteLength = 32;
    public const int EncodedRandomLength = 43;
    private const string HashPrefix = "sha256:";

    public GeneratedShareCapability Generate()
    {
        var capability = CapabilityPrefix + Base64UrlEncode(
            RandomNumberGenerator.GetBytes(CapabilityByteLength));
        return new GeneratedShareCapability(capability, Hash(capability));
    }

    public TokenHash Hash(string capability)
    {
        if (!HasValidFormat(capability))
        {
            throw new ArgumentException("The share capability is malformed.", nameof(capability));
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(capability));
        return TokenHash.FromHash(
            HashPrefix + Convert.ToHexString(digest).ToLowerInvariant());
    }

    private static bool HasValidFormat(string? capability) =>
        capability is not null &&
        capability.Length == CapabilityPrefix.Length + EncodedRandomLength &&
        capability.StartsWith(CapabilityPrefix, StringComparison.Ordinal) &&
        capability.AsSpan(CapabilityPrefix.Length).IndexOfAnyExcept(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".AsSpan()) < 0;

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
