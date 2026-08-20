using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public sealed class IssueAuthenticationTokens(
    AuthenticationTokenPolicy policy,
    IAccessTokenIssuer accessTokenIssuer,
    IRefreshTokenService refreshTokenService,
    IRefreshSessionRepository sessionRepository)
{
    public IssuedAuthenticationSession Execute(
        EntityId accountId,
        DateTimeOffset issuedAt,
        EntityId? familyId = null,
        EntityId? parentSessionId = null)
    {
        var sessionId = EntityId.New();
        var refreshToken = refreshTokenService.Generate();
        var refreshExpiresAt = issuedAt.Add(policy.RefreshTokenLifetime);
        var session = RefreshSession.Create(
            accountId,
            refreshToken.Hash,
            refreshExpiresAt,
            issuedAt,
            sessionId,
            familyId,
            parentSessionId);
        var accessToken = accessTokenIssuer.Issue(accountId, sessionId, issuedAt);

        sessionRepository.Add(session);
        return new IssuedAuthenticationSession(
            session,
            new AuthenticationTokenPair(
                accessToken.Value,
                refreshToken.Value,
                accessToken.ExpiresAt,
                refreshExpiresAt));
    }
}

public sealed record IssuedAuthenticationSession(
    RefreshSession Session,
    AuthenticationTokenPair Tokens);

public sealed record AuthenticationTokenPair(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt);
