namespace Beeexy.Application.Identity;

public interface IPrivateAccessSecretHasher
{
    string Hash(string secret);
    bool Verify(string secret, string? encodedHash);
    bool IsValidEncodedHash(string? encodedHash);
}
