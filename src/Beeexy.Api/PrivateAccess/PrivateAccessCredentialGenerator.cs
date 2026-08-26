using System.Security.Cryptography;

namespace Beeexy.Api.PrivateAccess;

internal static class PrivateAccessCredentialGenerator
{
    internal static readonly string[] Brands = ["Beeexy", "Bxy"];
    internal static readonly string[] HealthWords =
        ["Health", "Care", "Medical", "Clinic", "Wellness", "Vital", "Pulse"];
    internal static readonly string[] TechnologyWords =
        ["AI", "Tech", "Digital", "Smart", "Cloud"];
    private const string RandomAlphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private const string Symbols = "!#%+@";

    public static IReadOnlyList<PrivateAccessCredentialSuggestion> Generate(int count)
    {
        if (count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 100.");
        }

        var results = new List<PrivateAccessCredentialSuggestion>(count);
        var passwords = new HashSet<string>(StringComparer.Ordinal);
        while (results.Count < count)
        {
            var brand = Pick(Brands);
            var health = Pick(HealthWords);
            var technology = Pick(TechnologyWords);
            var username = RandomNumberGenerator.GetInt32(2) == 0
                ? brand + health
                : brand + health + technology;
            var keyword = RandomNumberGenerator.GetInt32(2) == 0
                ? health + technology
                : technology + health;
            var password = string.Concat(
                brand,
                health,
                PickCharacter(Symbols),
                RandomCharacters(12),
                RandomNumberGenerator.GetInt32(1000, 10_000),
                PickCharacter(Symbols));

            if (passwords.Add(password))
            {
                results.Add(new PrivateAccessCredentialSuggestion(username, password, keyword));
            }
        }

        return results;
    }

    private static string Pick(IReadOnlyList<string> values) =>
        values[RandomNumberGenerator.GetInt32(values.Count)];

    private static char PickCharacter(string values) =>
        values[RandomNumberGenerator.GetInt32(values.Length)];

    private static string RandomCharacters(int length)
    {
        return string.Create(length, 0, static (characters, _) =>
        {
            for (var index = 0; index < characters.Length; index++)
            {
                characters[index] = PickCharacter(RandomAlphabet);
            }
        });
    }
}

internal sealed record PrivateAccessCredentialSuggestion(
    string Username,
    string Password,
    string Keyword);
