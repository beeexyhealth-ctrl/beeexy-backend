using System.Security.Cryptography;
using System.Text;
using Beeexy.Application.Identity;
using Beeexy.Domain.Identity;

namespace Beeexy.Infrastructure.Identity;

public sealed class CryptographicRefreshTokenService : IRefreshTokenService
{
    private const string TokenPrefix = "rt1.";
    private const int TokenByteLength = 32;
    private const int MaximumTokenLength = 256;

    public GeneratedRefreshToken Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var token = TokenPrefix + Base64UrlEncode(bytes);
        return new GeneratedRefreshToken(token, Hash(token));
    }

    public TokenHash Hash(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        if (refreshToken.Length > MaximumTokenLength ||
            !refreshToken.StartsWith(TokenPrefix, StringComparison.Ordinal) ||
            refreshToken.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("The refresh token is malformed.", nameof(refreshToken));
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return TokenHash.FromHash($"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}");
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
