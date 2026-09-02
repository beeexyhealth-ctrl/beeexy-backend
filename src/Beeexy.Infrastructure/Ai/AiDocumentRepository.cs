using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Ai;

internal sealed class AiDocumentRepository(BeeexyDbContext dbContext) : IAiDocumentRepository
{
    public void Add(AiUploadedDocument document) => dbContext.AiUploadedDocuments.Add(document);

    public Task<AiUploadedDocument?> FindOwnedAsync(
        EntityId documentId,
        EntityId accountId,
        CancellationToken cancellationToken = default) =>
        dbContext.AiUploadedDocuments.SingleOrDefaultAsync(
            document => document.Id == documentId && document.AccountId == accountId,
            cancellationToken);

    public async Task<IReadOnlyList<AiUploadedDocument>> ListExpiredAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.AiUploadedDocuments
            .Where(document =>
                document.Status == AiDocumentStatus.Active && document.ExpiresAt <= now)
            .OrderBy(document => document.ExpiresAt)
            .ThenBy(document => document.Id)
            .Take(take)
            .ToArrayAsync(cancellationToken);

    public Task<DateTimeOffset?> GetNextExpiryAsync(
        CancellationToken cancellationToken = default) =>
        dbContext.AiUploadedDocuments.AsNoTracking()
            .Where(document => document.Status == AiDocumentStatus.Active)
            .MinAsync(document => (DateTimeOffset?)document.ExpiresAt, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
