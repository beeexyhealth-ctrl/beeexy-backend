using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Ai;

internal sealed class SecondOpinionRepository(BeeexyDbContext dbContext)
    : ISecondOpinionRepository
{
    private const long ExecutionLockNamespace = 0x4245455859414937;

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

    public async Task<SecondOpinionRegenerationSource?> FindRegenerationSourceAsync(
        EntityId analysisId,
        EntityId accountId,
        CancellationToken cancellationToken = default)
    {
        var request = await dbContext.AiAnalysisRequests.AsNoTracking()
            .Where(item =>
                item.Id == analysisId &&
                item.AccountId == accountId &&
                item.Purpose == AiAnalysisPurpose.SecondOpinion &&
                item.PatientProfileId != null)
            .Select(item => new
            {
                item.Id,
                PatientProfileId = item.PatientProfileId!.Value,
                item.OriginalInputSchemaVersion,
                item.OriginalInputSnapshotJson
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (request is null)
        {
            return null;
        }

        var currentSequence = await dbContext.AiResultSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.AnalysisRequestId == analysisId)
            .Select(snapshot => (int?)snapshot.Sequence)
            .MaxAsync(cancellationToken) ?? 0;
        return new SecondOpinionRegenerationSource(
            request.Id,
            request.PatientProfileId,
            request.OriginalInputSchemaVersion,
            request.OriginalInputSnapshotJson,
            checked(currentSequence + 1));
    }

    public async Task<ISecondOpinionExecutionLease?> TryAcquireExecutionLeaseAsync(
        EntityId analysisId,
        CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var lockKey = GetLockKey(analysisId.Value);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@lock_key)";
        AddParameter(command, "lock_key", lockKey);
        var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        if (!acquired)
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }

            return null;
        }

        var lease = new PostgreSqlExecutionLease(connection, lockKey, openedHere);
        try
        {
            var hasActiveExecution = await dbContext.AiExecutions.AsNoTracking().AnyAsync(
                execution =>
                    execution.AnalysisRequestId == analysisId &&
                    (execution.Status == AiExecutionStatus.Pending ||
                        execution.Status == AiExecutionStatus.Running),
                cancellationToken);
            if (!hasActiveExecution)
            {
                return lease;
            }

            await lease.DisposeAsync();
            return null;
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    public async Task<SecondOpinionStoredState> GetStateAsync(
        EntityId analysisId,
        CancellationToken cancellationToken = default)
    {
        var approved = await (
            from approvedSnapshot in dbContext.AiResultSnapshots.AsNoTracking()
            join approvedExecution in dbContext.AiExecutions.AsNoTracking()
                on approvedSnapshot.ExecutionId equals approvedExecution.Id
            join validation in dbContext.AiSafetyValidations.AsNoTracking()
                on new
                {
                    SnapshotId = approvedSnapshot.Id,
                    approvedSnapshot.ExecutionId
                }
                equals new
                {
                    SnapshotId = validation.ResultSnapshotId!.Value,
                    validation.ExecutionId
                }
            where approvedSnapshot.AnalysisRequestId == analysisId &&
                validation.DisplayEligible &&
                validation.Category == AiSafetyCategory.Approved &&
                validation.ResultSnapshotId != null
            orderby approvedSnapshot.Sequence descending,
                approvedSnapshot.CreatedAt descending,
                approvedSnapshot.Id descending
            select new SecondOpinionStoredState(
                approvedExecution.Status,
                approvedExecution.Id,
                approvedExecution.ProviderIdentifier,
                approvedExecution.ModelIdentifier,
                approvedExecution.PromptVersion,
                approvedSnapshot.ContentJson,
                approvedSnapshot.CreatedAt,
                validation.Category,
                validation.DisplayEligible,
                validation.ProductContentVersion))
            .FirstOrDefaultAsync(cancellationToken);
        if (approved is not null)
        {
            return approved;
        }

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

    private static long GetLockKey(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes[..8]) ^
            BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]) ^
            ExecutionLockNamespace;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed class PostgreSqlExecutionLease(
        DbConnection connection,
        long lockKey,
        bool closeConnection) : ISecondOpinionExecutionLease
    {
        private bool disposed;

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@lock_key)";
                AddParameter(command, "lock_key", lockKey);
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
            finally
            {
                if (closeConnection)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }

    private sealed record SafetyReadState(
        AiSafetyCategory Category,
        bool DisplayEligible,
        EntityId? ResultSnapshotId,
        string? ProductContentVersion);
}
