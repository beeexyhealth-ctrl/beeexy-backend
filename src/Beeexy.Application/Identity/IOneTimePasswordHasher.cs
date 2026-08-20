using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using System.Security.Cryptography;
using System.Text;

namespace Beeexy.Application.Identity;

public interface IOneTimePasswordHasher
{
    TokenHash Hash(EntityId challengeId, string oneTimeCode);

    bool Verify(EntityId challengeId, string oneTimeCode, TokenHash expectedHash)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);

        var actualBytes = Encoding.UTF8.GetBytes(Hash(challengeId, oneTimeCode).Value);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHash.Value);
        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
