using Beeexy.Application.Sharing;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Sharing;

internal sealed class SharedSecondOpinionReadRepository(BeeexyDbContext dbContext)
    : ISharedSecondOpinionReadRepository
{
    public async Task<IReadOnlyList<SharedSecondOpinionStoredResult>> ListDisplayableAsync(
        EntityId patientProfileId,
        CancellationToken cancellationToken = default)
    {
        var rows = await EligibleQuery(patientProfileId, null)
            .ToArrayAsync(cancellationToken);

        return rows
            .GroupBy(value => value.AnalysisId)
            .Select(group => group
                .OrderByDescending(value => value.Sequence)
                .ThenByDescending(value => value.GeneratedAt)
                .ThenByDescending(value => value.ResultId)
                .First())
            .OrderBy(value => value.GeneratedAt)
            .ThenBy(value => value.ResultId)
            .Select(ToStoredResult)
            .ToArray();
    }

    public async Task<SharedSecondOpinionStoredResult?> GetDisplayableAsync(
        EntityId patientProfileId,
        EntityId resultId,
        CancellationToken cancellationToken = default)
    {
        var rows = await EligibleQuery(patientProfileId, resultId)
            .ToArrayAsync(cancellationToken);
        var row = rows.SingleOrDefault();
        return row is null ? null : ToStoredResult(row);
    }

    private IQueryable<StoredRow> EligibleQuery(
        EntityId patientProfileId,
        EntityId? resultId) =>
        from snapshot in dbContext.AiResultSnapshots.AsNoTracking()
        join request in dbContext.AiAnalysisRequests.AsNoTracking()
            on snapshot.AnalysisRequestId equals request.Id
        join execution in dbContext.AiExecutions.AsNoTracking()
            on snapshot.ExecutionId equals execution.Id
        join validation in dbContext.AiSafetyValidations.AsNoTracking()
            on new { SnapshotId = snapshot.Id, snapshot.ExecutionId }
            equals new
            {
                SnapshotId = validation.ResultSnapshotId!.Value,
                validation.ExecutionId
            }
        where request.PatientProfileId == patientProfileId &&
            (!resultId.HasValue || snapshot.Id == resultId.Value) &&
            request.Purpose == AiAnalysisPurpose.SecondOpinion &&
            execution.Status == AiExecutionStatus.Succeeded &&
            validation.ResultSnapshotId != null &&
            validation.DisplayEligible &&
            validation.Category == AiSafetyCategory.Approved
        select new StoredRow(
            snapshot.Id,
            snapshot.AnalysisRequestId,
            snapshot.Sequence,
            snapshot.CreatedAt,
            snapshot.ResultSchemaVersion,
            snapshot.ContentJson);

    private static SharedSecondOpinionStoredResult ToStoredResult(StoredRow row) => new(
        row.ResultId,
        row.AnalysisId,
        row.GeneratedAt,
        row.ResultSchemaVersion,
        row.ContentJson);

    private sealed record StoredRow(
        EntityId ResultId,
        EntityId AnalysisId,
        int Sequence,
        DateTimeOffset GeneratedAt,
        string ResultSchemaVersion,
        string ContentJson);
}
