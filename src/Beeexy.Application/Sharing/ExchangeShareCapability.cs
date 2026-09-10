using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Sharing;

namespace Beeexy.Application.Sharing;

public sealed class ExchangeShareCapability(
    IShareCapabilityService capabilityService,
    IShareExchangeRepository repository,
    IShareAccessTokenIssuer tokenIssuer,
    ShareAccessTokenPolicy tokenPolicy,
    IClock clock)
{
    public async Task<ExchangeShareCapabilityResult> ExecuteAsync(
        string? capability,
        CancellationToken cancellationToken = default)
    {
        TokenHash capabilityHash;
        try
        {
            capabilityHash = capabilityService.Hash(capability ?? string.Empty);
        }
        catch (ArgumentException)
        {
            throw new ShareAccessDeniedException();
        }

        var state = await repository.FindByCapabilityHashAsync(
            capabilityHash,
            cancellationToken);
        var now = clock.UtcNow;
        if (state is null || !IsExchangeable(state, now))
        {
            throw new ShareAccessDeniedException();
        }

        var expiresAt = Min(now.Add(tokenPolicy.MaximumLifetime), state.Grant.ExpiresAt);
        if (expiresAt <= now)
        {
            throw new ShareAccessDeniedException();
        }

        var token = tokenIssuer.Issue(
            state.Grant.Id,
            state.Grant.Scope,
            now,
            expiresAt);
        return new ExchangeShareCapabilityResult(token.Value, token.ExpiresAt);
    }

    private static bool IsExchangeable(ShareExchangeState state, DateTimeOffset now)
    {
        var grant = state.Grant;
        if (grant.CreatedAt > now || grant.ExpiresAt <= now || grant.RevokedAt.HasValue)
        {
            return false;
        }

        return grant.Scope switch
        {
            ShareScope.FullProfile or ShareScope.PreTriage => state.ItemCount == 0,
            ShareScope.SpecificRecords => state.ItemCount > 0,
            ShareScope.Case or ShareScope.Visit => false,
            _ => false
        };
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;
}

public sealed record ExchangeShareCapabilityResult(
    string AccessToken,
    DateTimeOffset ExpiresAt);

public sealed class ShareAccessDeniedException()
    : Exception("The share capability is invalid or unavailable.");
