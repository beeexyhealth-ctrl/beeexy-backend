using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Identity;

public sealed class EmailAuthenticationChallengeRepository(BeeexyDbContext dbContext)
    : IEmailAuthenticationChallengeRepository
{
    public async Task ReplacePendingAsync(
        EmailAuthenticationChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);

        await dbContext.EmailAuthenticationChallenges
            .Where(candidate =>
                candidate.Email == challenge.Email &&
                candidate.Status == ChallengeStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.EmailAuthenticationChallenges.Add(challenge);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        EntityId challengeId,
        CancellationToken cancellationToken = default)
    {
        await dbContext.EmailAuthenticationChallenges
            .Where(challenge => challenge.Id == challengeId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
