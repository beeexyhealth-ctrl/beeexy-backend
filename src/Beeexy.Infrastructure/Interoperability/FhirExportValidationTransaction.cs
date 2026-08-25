using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.Interoperability;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Beeexy.Infrastructure.Interoperability;

internal sealed class FhirExportValidationTransaction(BeeexyDbContext dbContext)
    : IFhirExportValidationTransaction
{
    private IDbContextTransaction? transaction;

    public async Task BeginAsync(
        EntityId fhirExportId,
        CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException(
                "The FHIR validation transaction is already active.");
        }

        // Generation and validation intentionally compose in one HTTP request and
        // therefore share the scoped DbContext. Drop generation's committed tracked
        // snapshot so the validation advisory-lock winner always reloads the latest
        // lifecycle state written by a concurrent request.
        dbContext.ChangeTracker.Clear();
        transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var lockKey = CreateLockKey(fhirExportId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);
    }

    public async Task<FhirExportValidationState?> LoadAsync(
        EntityId patientProfileId,
        EntityId fhirExportId,
        CancellationToken cancellationToken = default)
    {
        EnsureActive();
        var export = await dbContext.FhirExports.SingleOrDefaultAsync(
            candidate =>
                candidate.Id == fhirExportId &&
                candidate.PatientProfileId == patientProfileId,
            cancellationToken);
        if (export is null)
        {
            return null;
        }

        var validationResult = await dbContext.FhirValidationResults
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.FhirExportId == export.Id,
                cancellationToken);
        return new FhirExportValidationState(export, validationResult);
    }

    public void Add(FhirValidationResult result)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(result);
        dbContext.FhirValidationResults.Add(result);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await transaction!.CommitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsync();
            transaction = null;
        }
    }

    private void EnsureActive()
    {
        if (transaction is null)
        {
            throw new InvalidOperationException(
                "The FHIR validation transaction is not active.");
        }
    }

    private static long CreateLockKey(EntityId fhirExportId)
    {
        Span<byte> identityBytes = stackalloc byte[16];
        fhirExportId.Value.TryWriteBytes(identityBytes);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(identityBytes, hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}
