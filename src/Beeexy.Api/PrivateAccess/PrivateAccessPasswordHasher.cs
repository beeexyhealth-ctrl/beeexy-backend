using System.Globalization;
using System.Security.Cryptography;

namespace Beeexy.Api.PrivateAccess;

internal static class PrivateAccessPasswordHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int DefaultIterations = 210_000;
    private const int MinimumIterations = 100_000;
    private const int MaximumIterations = 2_000_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return string.Join(
            '$',
            Algorithm,
            DefaultIterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public static bool Verify(string secret, string encodedHash)
    {
        if (secret is null || !TryParse(encodedHash, out var parsed))
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            secret,
            parsed.Salt,
            parsed.Iterations,
            HashAlgorithmName.SHA256,
            parsed.Hash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, parsed.Hash);
    }

    public static bool IsValidEncodedHash(string? encodedHash)
    {
        return TryParse(encodedHash, out _);
    }

    private static bool TryParse(string? encodedHash, out ParsedHash parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('$');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], Algorithm, StringComparison.Ordinal) ||
            !int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var iterations) ||
            iterations is < MinimumIterations or > MaximumIterations)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var hash = Convert.FromBase64String(parts[3]);
            if (salt.Length < SaltSize || hash.Length != HashSize)
            {
                return false;
            }

            parsed = new ParsedHash(iterations, salt, hash);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private readonly record struct ParsedHash(int Iterations, byte[] Salt, byte[] Hash);
}
