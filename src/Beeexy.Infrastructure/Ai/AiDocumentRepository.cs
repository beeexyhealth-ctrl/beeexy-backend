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
        AiDocumentExpiryCursor? after = null,
        CancellationToken cancellationToken = default)
    {
        var query = after is null
            ? dbContext.AiUploadedDocuments.FromSqlInterpolated($"""
                SELECT document.*
                FROM ai.ai_uploaded_documents AS document
                WHERE document.status = 'active'
                  AND document.expires_at <= {now}
                ORDER BY document.expires_at, document.id
                LIMIT {take}
                """)
            : dbContext.AiUploadedDocuments.FromSqlInterpolated($"""
                SELECT document.*
                FROM ai.ai_uploaded_documents AS document
                WHERE document.status = 'active'
                  AND document.expires_at <= {now}
                  AND (
                    document.expires_at > {after.ExpiresAt}
                    OR (
                      document.expires_at = {after.ExpiresAt}
                      AND document.id > {after.DocumentId.Value}
                    )
                  )
                ORDER BY document.expires_at, document.id
                LIMIT {take}
                """);
        return await query
            .OrderBy(document => document.ExpiresAt)
            .ThenBy(document => document.Id)
            .ToArrayAsync(cancellationToken);
    }

    public Task<DateTimeOffset?> GetNextExpiryAsync(
        DateTimeOffset? strictlyAfter = null,
        CancellationToken cancellationToken = default) =>
        dbContext.AiUploadedDocuments.AsNoTracking()
            .Where(document =>
                document.Status == AiDocumentStatus.Active &&
                (!strictlyAfter.HasValue || document.ExpiresAt > strictlyAfter.Value))
            .MinAsync(document => (DateTimeOffset?)document.ExpiresAt, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
