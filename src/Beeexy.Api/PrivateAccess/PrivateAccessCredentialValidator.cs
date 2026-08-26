using System.Security.Cryptography;
using System.Text;

namespace Beeexy.Api.PrivateAccess;

internal sealed class PrivateAccessCredentialValidator(PrivateAccessSettings settings)
{
    public bool Validate(string username, string password, string keyword)
    {
        if (!settings.Enabled)
        {
            return true;
        }

        var configuredUsername = SHA256.HashData(Encoding.UTF8.GetBytes(settings.Username!));
        var suppliedUsername = SHA256.HashData(Encoding.UTF8.GetBytes(username));
        var usernameMatches = CryptographicOperations.FixedTimeEquals(
            configuredUsername,
            suppliedUsername);
        var passwordMatches = PrivateAccessPasswordHasher.Verify(password, settings.PasswordHash!);
        var keywordMatches = PrivateAccessPasswordHasher.Verify(keyword, settings.KeywordHash!);

        return usernameMatches & passwordMatches & keywordMatches;
    }
}
