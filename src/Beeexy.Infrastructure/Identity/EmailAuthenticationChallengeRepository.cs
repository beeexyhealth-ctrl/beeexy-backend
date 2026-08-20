using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Identity;

public sealed class EmailAuthenticationChallengeRepository(BeeexyDbContext dbContext)
    : IEmailAuthenticationChallengeRepository
{
    public async Task<EmailAuthenticationChallenge?> FindLatestForUpdateAsync(
        NormalizedEmail email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        var challenges = await dbContext.EmailAuthenticationChallenges
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM identity.email_authentication_challenges
                WHERE normalized_email = {email.Value}
                ORDER BY created_at DESC
                LIMIT 1
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return challenges.SingleOrDefault();
    }

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
