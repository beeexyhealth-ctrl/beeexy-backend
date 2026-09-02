using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Ai;

internal sealed class SecondOpinionRepository(BeeexyDbContext dbContext)
    : ISecondOpinionRepository
{
    public void Add(AiAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        dbContext.AiAnalysisRequests.Add(request);
    }

    public Task<SecondOpinionAnalysisAccess?> FindOwnedAsync(
        EntityId analysisId,
        EntityId accountId,
        CancellationToken cancellationToken = default) =>
        dbContext.AiAnalysisRequests.AsNoTracking()
            .Where(request =>
                request.Id == analysisId &&
                request.AccountId == accountId &&
                request.Purpose == AiAnalysisPurpose.SecondOpinion &&
                request.PatientProfileId != null)
            .Select(request => new SecondOpinionAnalysisAccess(
                request.Id,
                request.PatientProfileId!.Value))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<SecondOpinionStoredState> GetStateAsync(
        EntityId analysisId,
        CancellationToken cancellationToken = default)
    {
        var execution = await dbContext.AiExecutions.AsNoTracking()
            .Where(item => item.AnalysisRequestId == analysisId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (execution is null)
        {
            return new SecondOpinionStoredState(
                null, null, null, null, null, null, null, null, null, null);
        }

        var safety = await dbContext.AiSafetyValidations.AsNoTracking()
            .Where(validation => validation.ExecutionId == execution.Id)
            .Select(validation => new SafetyReadState(
                validation.Category,
                validation.DisplayEligible,
                validation.ResultSnapshotId,
                validation.ProductContentVersion))
            .SingleOrDefaultAsync(cancellationToken);
        AiResultSnapshot? snapshot = null;
        if (safety is { DisplayEligible: true, ResultSnapshotId: { } snapshotId })
        {
            snapshot = await dbContext.AiResultSnapshots.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == snapshotId, cancellationToken);
        }

        return new SecondOpinionStoredState(
            execution.Status,
            execution.Id,
            execution.ProviderIdentifier,
            execution.ModelIdentifier,
            execution.PromptVersion,
            snapshot?.ContentJson,
            snapshot?.CreatedAt,
            safety?.Category,
            safety?.DisplayEligible,
            safety?.ProductContentVersion);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private sealed record SafetyReadState(
        AiSafetyCategory Category,
        bool DisplayEligible,
        EntityId? ResultSnapshotId,
        string? ProductContentVersion);
}
