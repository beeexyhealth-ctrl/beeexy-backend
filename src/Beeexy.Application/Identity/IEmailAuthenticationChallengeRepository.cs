using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public interface IEmailAuthenticationChallengeRepository
{
    Task<EmailAuthenticationChallenge?> FindLatestForUpdateAsync(
        NormalizedEmail email,
        CancellationToken cancellationToken = default);

    Task ReplacePendingAsync(
        EmailAuthenticationChallenge challenge,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        EntityId challengeId,
        CancellationToken cancellationToken = default);
}
