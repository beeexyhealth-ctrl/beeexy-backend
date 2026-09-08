using System.Data;
using Beeexy.Application.Care;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Beeexy.Infrastructure.Care;

internal sealed class SymptomCheckInTransaction(BeeexyDbContext dbContext)
    : ISymptomCheckInTransaction
{
    private const string IdempotencyConstraint =
        "ux_symptom_check_ins_episode_idempotency";

    private IDbContextTransaction? transaction;

    public async Task BeginAsync(
        EntityId episodeId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException(
                "The symptom check-in transaction is already active.");
        }

        transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var lockKey = $"symptom-check-in:{episodeId.Value:D}:{idempotencyKey.Value:D}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 95));",
            cancellationToken);
    }

    public async Task<SymptomCheckInEpisodeSource?> FindEpisodeAsync(
        EntityId episodeId,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionActive();
        var episodes = await dbContext.PreTriageEpisodes
            .FromSqlInterpolated($"""
                SELECT * FROM triage.pre_triage_episodes
                WHERE id = {episodeId.Value}
                FOR SHARE
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var episode = episodes.SingleOrDefault();
        if (episode?.PatientProfileId is null)
        {
            return null;
        }

        var questionnaire = await dbContext.QuestionnaireVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == episode.QuestionnaireVersionId,
                cancellationToken);
        return questionnaire is null
            ? null
            : new SymptomCheckInEpisodeSource(episode, questionnaire);
    }

    public Task<SymptomCheckIn?> FindExistingAsync(
        EntityId episodeId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionActive();
        return FindExistingCoreAsync(episodeId, idempotencyKey, cancellationToken);
    }

    public async Task<SymptomDiaryPackageVersion?> FindPackageAsync(
        EntityId packageVersionId,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionActive();
        return await dbContext.SymptomDiaryPackageVersions
            .AsNoTracking()
            .AsSplitQuery()
            .Include(package => package.Questions)
            .ThenInclude(question => question.Options)
            .SingleOrDefaultAsync(
                package => package.Id == packageVersionId,
                cancellationToken);
    }

    public void Add(SymptomCheckIn checkIn)
    {
        EnsureTransactionActive();
        dbContext.SymptomCheckIns.Add(checkIn);
    }

    public async Task<SymptomCheckInSaveResult> SaveAsync(
        SymptomCheckIn checkIn,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionActive();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new SymptomCheckInSaveResult(checkIn, NewlyCreated: true);
        }
        catch (DbUpdateException exception) when (IsIdempotencyViolation(exception))
        {
            await RollbackAndClearAsync(cancellationToken);
            var existing = await FindExistingCoreAsync(
                checkIn.EpisodeId,
                checkIn.IdempotencyKey,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return new SymptomCheckInSaveResult(existing, NewlyCreated: false);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (transaction is null)
        {
            return;
        }

        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsync();
            transaction = null;
        }
    }

    private Task<SymptomCheckIn?> FindExistingCoreAsync(
        EntityId episodeId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken) =>
        dbContext.SymptomCheckIns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                checkIn => checkIn.EpisodeId == episodeId &&
                    checkIn.IdempotencyKey == idempotencyKey,
                cancellationToken);

    private async Task RollbackAndClearAsync(CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            transaction = null;
        }

        dbContext.ChangeTracker.Clear();
    }

    private void EnsureTransactionActive()
    {
        if (transaction is null)
        {
            throw new InvalidOperationException(
                "A symptom check-in transaction has not been started.");
        }
    }

    private static bool IsIdempotencyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: IdempotencyConstraint
        };
}
