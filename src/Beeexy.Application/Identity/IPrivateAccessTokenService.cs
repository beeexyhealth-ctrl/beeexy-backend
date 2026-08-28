using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public interface IPrivateAccessTokenService
{
    GeneratedPrivateAccessToken Generate();
    TokenHash Hash(string token);
}

public sealed record GeneratedPrivateAccessToken(string Value, TokenHash Hash);
