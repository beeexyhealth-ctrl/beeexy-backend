using System.Security.Cryptography;
using Beeexy.Application.Identity;
using Beeexy.Domain.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Beeexy.Infrastructure.Identity;

public sealed class CryptographicPrivateAccessTokenService : IPrivateAccessTokenService
{
    public GeneratedPrivateAccessToken Generate()
    {
        var value = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return new GeneratedPrivateAccessToken(value, Hash(value));
    }

    public TokenHash Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return TokenHash.FromHash(Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))));
    }
}
