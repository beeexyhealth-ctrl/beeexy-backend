using System.Security.Claims;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;

namespace Beeexy.Api.Identity;

internal sealed class HttpCurrentSessionIdentity(IHttpContextAccessor httpContextAccessor)
    : ICurrentSessionIdentity
{
    public CurrentSessionIdentity GetRequired()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var accountValue = principal?.FindFirstValue("sub");
        var sessionValue = principal?.FindFirstValue("sid");

        if (!Guid.TryParse(accountValue, out var accountId) ||
            !Guid.TryParse(sessionValue, out var sessionId))
        {
            throw new SessionAuthenticationException();
        }

        return new CurrentSessionIdentity(
            EntityId.From(accountId),
            EntityId.From(sessionId));
    }
}
