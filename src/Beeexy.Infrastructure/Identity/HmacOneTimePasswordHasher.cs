using System.Security.Cryptography;
using System.Text;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Infrastructure.Identity;

public sealed class HmacOneTimePasswordHasher : IOneTimePasswordHasher
{
    private const int MinimumKeyLength = 32;
    private readonly byte[] _key;

    public HmacOneTimePasswordHasher(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _key = Encoding.UTF8.GetBytes(key);
        if (_key.Length < MinimumKeyLength)
        {
            throw new ArgumentException(
                $"The OTP hashing key must contain at least {MinimumKeyLength} bytes.",
                nameof(key));
        }
    }

    public TokenHash Hash(EntityId challengeId, string oneTimeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oneTimeCode);

        var value = $"{challengeId.Value:N}:{oneTimeCode}";
        var digest = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value));
        var encoded = Convert.ToHexString(digest).ToLowerInvariant();
        return TokenHash.FromHash($"hmac-sha256:{encoded}");
    }
}
