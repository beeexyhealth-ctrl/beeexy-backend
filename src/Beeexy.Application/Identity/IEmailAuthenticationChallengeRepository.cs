using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public interface IEmailAuthenticationChallengeRepository
{
    Task ReplacePendingAsync(
        EmailAuthenticationChallenge challenge,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        EntityId challengeId,
        CancellationToken cancellationToken = default);
}
