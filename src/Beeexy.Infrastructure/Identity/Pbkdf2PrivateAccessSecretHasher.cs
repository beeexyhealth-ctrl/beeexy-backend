using System.Globalization;
using System.Security.Cryptography;
using Beeexy.Application.Identity;

namespace Beeexy.Infrastructure.Identity;

public sealed class Pbkdf2PrivateAccessSecretHasher : IPrivateAccessSecretHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int DefaultIterations = 210_000;
    private const int MinimumIterations = 100_000;
    private const int MaximumIterations = 2_000_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string DummyHash =
        "pbkdf2-sha256$210000$AAAAAAAAAAAAAAAAAAAAAA==$" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    public string Hash(string secret)
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

    public bool Verify(string secret, string? encodedHash)
    {
        var selectedHash = TryParse(encodedHash, out var parsed)
            ? parsed
            : ParseRequired(DummyHash);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            secret ?? string.Empty,
            selectedHash.Salt,
            selectedHash.Iterations,
            HashAlgorithmName.SHA256,
            selectedHash.Hash.Length);
        return encodedHash is not null &&
            CryptographicOperations.FixedTimeEquals(actualHash, selectedHash.Hash);
    }

    public bool IsValidEncodedHash(string? encodedHash) => TryParse(encodedHash, out _);

    private static ParsedHash ParseRequired(string value)
    {
        _ = TryParse(value, out var parsed);
        return parsed;
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
            parts[0] != Algorithm ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
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
