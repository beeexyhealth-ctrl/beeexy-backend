using Beeexy.Application.Care;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Care;

internal sealed class SymptomCheckInReadRepository(BeeexyDbContext dbContext)
    : ISymptomCheckInReadRepository
{
    public Task<bool> CursorExistsAsync(
        SymptomCheckInPageCursor cursor,
        CancellationToken cancellationToken = default) =>
        dbContext.SymptomCheckIns
            .AsNoTracking()
            .AnyAsync(checkIn =>
                checkIn.EpisodeId == cursor.EpisodeId &&
                checkIn.Id == cursor.CheckInId &&
                checkIn.CreatedAt == cursor.CreatedAt,
                cancellationToken);

    public async Task<IReadOnlyList<SymptomCheckInHistoryRecord>> ListAsync(
        EntityId episodeId,
        SymptomCheckInPageCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        var checkIns = await BuildQuery(episodeId, after, take)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        if (checkIns.Length == 0)
        {
            return [];
        }

        var ids = checkIns.Select(checkIn => checkIn.Id).ToArray();
        var answers = await (
            from answer in dbContext.SymptomCheckInAnswers.AsNoTracking()
            join question in dbContext.SymptomDiaryQuestions.AsNoTracking()
                on new { answer.QuestionId, answer.PackageVersionId }
                equals new { QuestionId = question.Id, question.PackageVersionId }
            where ids.Contains(answer.CheckInId)
            orderby answer.CheckInId, answer.SourceOrder
            select new AnswerProjection(
                answer.CheckInId,
                answer.PackageVersionId,
                question.Code,
                answer.SourceOrder,
                answer.SubmittedValueJson))
            .ToArrayAsync(cancellationToken);
        var groupedAnswers = answers.ToLookup(answer => answer.CheckInId);

        return checkIns.Select(checkIn => new SymptomCheckInHistoryRecord(
            checkIn.Id,
            checkIn.EpisodeId,
            checkIn.PackageVersionId,
            checkIn.CreatedAt,
            groupedAnswers[checkIn.Id]
                .Select(answer =>
                {
                    if (answer.PackageVersionId != checkIn.PackageVersionId)
                    {
                        throw new SymptomDiaryContentUnavailableException();
                    }

                    return new SymptomCheckInHistoryAnswerRecord(
                        answer.QuestionCode,
                        answer.SourceOrder,
                        answer.SubmittedValueJson);
                })
                .ToArray()))
            .ToArray();
    }

    private IQueryable<SymptomCheckIn> BuildQuery(
        EntityId episodeId,
        SymptomCheckInPageCursor? after,
        int take)
    {
        if (after is null)
        {
            return dbContext.SymptomCheckIns.FromSqlInterpolated($"""
                SELECT check_in.*
                FROM care.symptom_check_ins AS check_in
                WHERE check_in.episode_id = {episodeId.Value}
                ORDER BY check_in.created_at ASC, check_in.id ASC
                LIMIT {take}
                """);
        }

        return dbContext.SymptomCheckIns.FromSqlInterpolated($"""
            SELECT check_in.*
            FROM care.symptom_check_ins AS check_in
            WHERE check_in.episode_id = {episodeId.Value}
              AND (
                check_in.created_at > {after.CreatedAt}
                OR (
                  check_in.created_at = {after.CreatedAt}
                  AND check_in.id > {after.CheckInId.Value}
                )
              )
            ORDER BY check_in.created_at ASC, check_in.id ASC
            LIMIT {take}
            """);
    }

    private sealed record AnswerProjection(
        EntityId CheckInId,
        EntityId PackageVersionId,
        SymptomDiaryCode QuestionCode,
        int SourceOrder,
        string SubmittedValueJson);
}
