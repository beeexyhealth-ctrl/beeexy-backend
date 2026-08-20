using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public interface IRefreshTokenService
{
    GeneratedRefreshToken Generate();

    TokenHash Hash(string refreshToken);
}

public sealed record GeneratedRefreshToken(string Value, TokenHash Hash);
