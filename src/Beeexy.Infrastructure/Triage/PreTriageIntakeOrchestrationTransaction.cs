using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Triage;

public sealed class PreTriageIntakeOrchestrationTransaction(BeeexyDbContext dbContext)
    : IPreTriageIntakeOrchestrationTransaction
{
    public async Task<PreTriageIntakeTransactionResult<TResult>> ExecuteAsync<TResult>(
        string operationKeyHash,
        string? reservationAliasHash,
        string requestFingerprint,
        Func<CancellationToken, Task<PreTriageIntakeTransactionCommit<TResult>>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKeyHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        ArgumentNullException.ThrowIfNull(operation);
        if (dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "Pre-triage intake orchestration cannot start inside another transaction.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        var lockKey = CreateLockKey(reservationAliasHash ?? operationKeyHash);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);
        var existing = await dbContext.PreTriageIntakeIdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.OperationKeyHash == operationKeyHash ||
                    (reservationAliasHash != null &&
                        value.ReservationAliasHash == reservationAliasHash),
                cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(
                    existing.RequestFingerprint,
                    requestFingerprint,
                    StringComparison.Ordinal))
            {
                throw new PreTriageIntakeIdempotencyConflictException();
            }

            await transaction.CommitAsync(cancellationToken);
            return new PreTriageIntakeTransactionResult<TResult>(
                default,
                new PreTriageIntakeReplayReference(
                    existing.SessionId,
                    existing.InitialAnswerCodes));
        }

        var result = await operation(cancellationToken);
        if (result.SessionId.HasValue)
        {
            dbContext.PreTriageIntakeIdempotencyRecords.Add(
                PreTriageIntakeIdempotencyRecord.CreateCompleted(
                    operationKeyHash,
                    reservationAliasHash,
                    requestFingerprint,
                    result.SessionId.Value,
                    result.InitialAnswerCodes,
                    result.CreatedAt!.Value,
                    result.CompletedAt!.Value));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new PreTriageIntakeTransactionResult<TResult>(result.Result, null);
    }

    private static long CreateLockKey(string operationKeyHash)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(operationKeyHash), hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}
