using System.Globalization;
using System.Security.Cryptography;
using Beeexy.Application.Identity;

namespace Beeexy.Infrastructure.Identity;

public sealed class CryptographicOneTimePasswordGenerator : IOneTimePasswordGenerator
{
    public string Generate(int length)
    {
        if (length is < 6 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var upperExclusive = checked((int)Math.Pow(10, length));
        var value = RandomNumberGenerator.GetInt32(upperExclusive);
        return value.ToString($"D{length}", CultureInfo.InvariantCulture);
    }
}
