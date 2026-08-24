using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Interoperability;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Beeexy.Infrastructure.Interoperability;

internal sealed class FhirExportGenerationTransaction(BeeexyDbContext dbContext)
    : IFhirExportGenerationTransaction
{
    private IDbContextTransaction? transaction;

    public async Task BeginAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException("The FHIR export transaction is already active.");
        }

        transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var lockKey = CreateLockKey(patientProfileId, idempotencyKey);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);
    }

    public async Task<FhirExportAuthoritativeSource?> LoadAuthoritativeSourceAsync(
        EntityId patientProfileId,
        EntityId sourceClinicalHistoryEventId,
        CancellationToken cancellationToken = default)
    {
        EnsureActive();
        var historyEvent = await dbContext.ClinicalHistoryEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == sourceClinicalHistoryEventId &&
                    candidate.PatientProfileId == patientProfileId,
                cancellationToken);
        if (historyEvent is null ||
            historyEvent.EventType != ClinicalHistoryEventType.CompletedPreTriage)
        {
            return null;
        }

        var episode = await dbContext.PreTriageEpisodes
            .AsNoTracking()
            .Include(candidate => candidate.Answers)
            .Include(candidate => candidate.ReportedSymptoms)
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == historyEvent.SourceId &&
                    candidate.PatientProfileId == patientProfileId,
                cancellationToken);
        if (episode is null ||
            episode.QuestionnaireVersionId != historyEvent.SourceQuestionnaireVersionId ||
            episode.ClinicalRuleSetVersionId !=
                historyEvent.SourceClinicalRuleSetVersionId ||
            episode.CompletedAt != historyEvent.OccurredAt)
        {
            return null;
        }

        var assessment = await dbContext.ClinicalAssessments
            .AsNoTracking()
            .Include(candidate => candidate.Findings)
            .SingleOrDefaultAsync(
                candidate => candidate.EpisodeId == episode.Id,
                cancellationToken);
        var questionnaire = await dbContext.QuestionnaireVersions
            .AsNoTracking()
            .Include(candidate => candidate.Questions)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == episode.QuestionnaireVersionId,
                cancellationToken);
        if (assessment is null ||
            assessment.ClinicalRuleSetVersionId != episode.ClinicalRuleSetVersionId ||
            questionnaire is null)
        {
            return null;
        }

        return new FhirExportAuthoritativeSource(
            historyEvent,
            episode,
            assessment,
            questionnaire);
    }

    public Task<FhirExport?> FindByIdempotencyKeyAsync(
        EntityId patientProfileId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureActive();
        return dbContext.FhirExports.SingleOrDefaultAsync(
            candidate =>
                candidate.PatientProfileId == patientProfileId &&
                candidate.IdempotencyKey == idempotencyKey,
            cancellationToken);
    }

    public void Add(FhirExport export)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(export);
        dbContext.FhirExports.Add(export);
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
            throw new InvalidOperationException("The FHIR export transaction is not active.");
        }
    }

    private static long CreateLockKey(EntityId patientProfileId, EntityId idempotencyKey)
    {
        Span<byte> identityBytes = stackalloc byte[32];
        patientProfileId.Value.TryWriteBytes(identityBytes[..16]);
        idempotencyKey.Value.TryWriteBytes(identityBytes[16..]);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(identityBytes, hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}
