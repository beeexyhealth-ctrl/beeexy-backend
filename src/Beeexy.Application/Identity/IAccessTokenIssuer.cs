using Beeexy.Domain.Common;

namespace Beeexy.Application.Identity;

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(
        EntityId accountId,
        EntityId sessionId,
        DateTimeOffset issuedAt);
}

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);
