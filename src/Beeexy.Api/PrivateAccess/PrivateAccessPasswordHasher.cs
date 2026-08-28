using Beeexy.Infrastructure.Identity;

namespace Beeexy.Api.PrivateAccess;

internal static class PrivateAccessPasswordHasher
{
    private static readonly Pbkdf2PrivateAccessSecretHasher Hasher = new();

    public static string Hash(string secret)
    {
        return Hasher.Hash(secret);
    }

    public static bool Verify(string secret, string encodedHash)
    {
        return Hasher.Verify(secret, encodedHash);
    }

    public static bool IsValidEncodedHash(string? encodedHash)
    {
        return Hasher.IsValidEncodedHash(encodedHash);
    }
}
