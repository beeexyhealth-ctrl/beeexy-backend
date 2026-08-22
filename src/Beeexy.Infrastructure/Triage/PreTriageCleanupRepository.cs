using System.Data;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageCleanupRepository(BeeexyDbContext dbContext)
    : IPreTriageCleanupRepository
{
    public async Task<IReadOnlyList<PreTriageCleanupCandidate>> FindCandidatesAsync(
        DateTimeOffset cutoff,
        int batchSize,
        PreTriageCleanupCursor? after,
        CancellationToken cancellationToken = default)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT candidate.session_id, candidate.category, candidate.eligible_at
                FROM (
                    SELECT session.id AS session_id,
                           CASE WHEN session.patient_profile_id IS NULL THEN 0 ELSE 2 END AS category,
                           session.expires_at AS eligible_at
                    FROM triage.pre_triage_sessions AS session
                    WHERE session.status = 'active'
                      AND session.expires_at <= @cutoff

                    UNION ALL

                    SELECT session.id AS session_id,
                           1 AS category,
                           episode.anonymous_expires_at AS eligible_at
                    FROM triage.pre_triage_episodes AS episode
                    INNER JOIN triage.pre_triage_sessions AS session
                        ON session.id = episode.source_session_id
                       AND session.questionnaire_version_id = episode.questionnaire_version_id
                    WHERE session.status = 'completed'
                      AND session.patient_profile_id IS NULL
                      AND episode.patient_profile_id IS NULL
                      AND episode.claimed_at IS NULL
                      AND episode.anonymous_expires_at <= @cutoff
                ) AS candidate
                WHERE @after_eligible_at IS NULL
                   OR candidate.eligible_at > @after_eligible_at
                   OR (candidate.eligible_at = @after_eligible_at
                       AND candidate.session_id > @after_session_id)
                ORDER BY candidate.eligible_at, candidate.session_id
                LIMIT @batch_size;
                """;
            command.Parameters.Add(new NpgsqlParameter(
                "cutoff",
                NpgsqlDbType.TimestampTz)
            {
                Value = cutoff
            });
            command.Parameters.Add(new NpgsqlParameter(
                "after_eligible_at",
                NpgsqlDbType.TimestampTz)
            {
                Value = after is null ? DBNull.Value : after.EligibleAt
            });
            command.Parameters.Add(new NpgsqlParameter(
                "after_session_id",
                NpgsqlDbType.Uuid)
            {
                Value = after is null ? DBNull.Value : after.SessionId.Value
            });
            command.Parameters.Add(new NpgsqlParameter(
                "batch_size",
                NpgsqlDbType.Integer)
            {
                Value = batchSize
            });

            var candidates = new List<PreTriageCleanupCandidate>(batchSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var categoryValue = reader.GetInt32(1);
                if (!Enum.IsDefined(typeof(PreTriageCleanupCategory), categoryValue))
                {
                    throw new InvalidOperationException(
                        "The pre-triage cleanup candidate category is invalid.");
                }

                candidates.Add(new PreTriageCleanupCandidate(
                    EntityId.From(reader.GetGuid(0)),
                    (PreTriageCleanupCategory)categoryValue,
                    reader.GetFieldValue<DateTimeOffset>(2)));
            }

            return candidates;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<PreTriageCleanupOutcome> CleanupLockedAsync(
        PreTriageCleanupCandidate candidate,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        var session = await dbContext.PreTriageSessions
            .FromSqlInterpolated(
                $"SELECT * FROM triage.pre_triage_sessions WHERE id = {candidate.SessionId.Value} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return await CommitAsync(
                transaction,
                PreTriageCleanupOutcome.AlreadyAbsent,
                cancellationToken);
        }

        var episode = await dbContext.PreTriageEpisodes
            .FromSqlInterpolated(
                $"SELECT * FROM triage.pre_triage_episodes WHERE source_session_id = {candidate.SessionId.Value} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        var outcome = candidate.Category switch
        {
            PreTriageCleanupCategory.AnonymousActive =>
                await CleanupActiveSessionAsync(
                    session,
                    episode,
                    cutoff,
                    anonymous: true,
                    cancellationToken),
            PreTriageCleanupCategory.AuthenticatedAbandoned =>
                await CleanupActiveSessionAsync(
                    session,
                    episode,
                    cutoff,
                    anonymous: false,
                    cancellationToken),
            PreTriageCleanupCategory.AnonymousCompletedUnclaimed =>
                await CleanupCompletedAnonymousAsync(
                    session,
                    episode,
                    cutoff,
                    cancellationToken),
            _ => throw new InvalidOperationException(
                "The pre-triage cleanup category is invalid.")
        };

        return await CommitAsync(transaction, outcome, cancellationToken);
    }

    private async Task<PreTriageCleanupOutcome> CleanupActiveSessionAsync(
        PreTriageSession session,
        PreTriageEpisode? episode,
        DateTimeOffset cutoff,
        bool anonymous,
        CancellationToken cancellationToken)
    {
        if (session.Status != PreTriageSessionStatus.Active ||
            session.ExpiresAt > cutoff ||
            session.IsAnonymous != anonymous)
        {
            return IsPermanent(session, episode)
                ? PreTriageCleanupOutcome.PreservedPermanent
                : PreTriageCleanupOutcome.SkippedAfterRevalidation;
        }

        if (episode is not null)
        {
            return PreTriageCleanupOutcome.PreservedPermanent;
        }

        var removed = await dbContext.PreTriageSessions
            .Where(value =>
                value.Id == session.Id &&
                value.Status == PreTriageSessionStatus.Active &&
                value.ExpiresAt <= cutoff &&
                (anonymous
                    ? value.PatientProfileId == null
                    : value.PatientProfileId != null))
            .ExecuteDeleteAsync(cancellationToken);
        return removed == 1
            ? PreTriageCleanupOutcome.Removed
            : PreTriageCleanupOutcome.SkippedAfterRevalidation;
    }

    private async Task<PreTriageCleanupOutcome> CleanupCompletedAnonymousAsync(
        PreTriageSession session,
        PreTriageEpisode? episode,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        if (episode?.PatientProfileId is not null || episode?.ClaimedAt is not null)
        {
            return PreTriageCleanupOutcome.PreservedPermanent;
        }

        if (session.Status != PreTriageSessionStatus.Completed ||
            !session.IsAnonymous ||
            episode is null ||
            !episode.AnonymousExpiresAt.HasValue ||
            episode.AnonymousExpiresAt.Value > cutoff ||
            episode.AnonymousExpiresAt != session.ExpiresAt ||
            episode.SourceSessionId != session.Id ||
            episode.QuestionnaireVersionId != session.QuestionnaireVersionId)
        {
            return PreTriageCleanupOutcome.SkippedAfterRevalidation;
        }

        var assessment = await dbContext.ClinicalAssessments
            .FromSqlInterpolated(
                $"SELECT * FROM triage.clinical_assessments WHERE episode_id = {episode.Id.Value} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (assessment is null ||
            assessment.ClinicalRuleSetVersionId != episode.ClinicalRuleSetVersionId)
        {
            throw new InvalidOperationException(
                "The expired anonymous pre-triage graph is inconsistent.");
        }

        await dbContext.ClinicalFindings
            .Where(value => value.AssessmentId == assessment.Id)
            .ExecuteDeleteAsync(cancellationToken);
        var assessmentsRemoved = await dbContext.ClinicalAssessments
            .Where(value => value.Id == assessment.Id && value.EpisodeId == episode.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.TriageAnswers
            .Where(value => value.EpisodeId == episode.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ReportedSymptoms
            .Where(value => value.EpisodeId == episode.Id)
            .ExecuteDeleteAsync(cancellationToken);
        var episodesRemoved = await dbContext.PreTriageEpisodes
            .Where(value =>
                value.Id == episode.Id &&
                value.SourceSessionId == session.Id &&
                value.PatientProfileId == null &&
                value.ClaimedAt == null &&
                value.AnonymousExpiresAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var sessionsRemoved = await dbContext.PreTriageSessions
            .Where(value =>
                value.Id == session.Id &&
                value.Status == PreTriageSessionStatus.Completed &&
                value.PatientProfileId == null)
            .ExecuteDeleteAsync(cancellationToken);

        if (assessmentsRemoved != 1 || episodesRemoved != 1 || sessionsRemoved != 1)
        {
            throw new InvalidOperationException(
                "The expired anonymous pre-triage graph was not removed atomically.");
        }

        return PreTriageCleanupOutcome.Removed;
    }

    private static bool IsPermanent(
        PreTriageSession session,
        PreTriageEpisode? episode) =>
        session.Status == PreTriageSessionStatus.Completed &&
        (session.PatientProfileId.HasValue || episode?.PatientProfileId.HasValue == true);

    private static async Task<PreTriageCleanupOutcome> CommitAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        PreTriageCleanupOutcome outcome,
        CancellationToken cancellationToken)
    {
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }
}
