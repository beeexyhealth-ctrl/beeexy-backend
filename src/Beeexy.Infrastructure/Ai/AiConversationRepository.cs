using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Ai;

internal sealed class AiConversationRepository(BeeexyDbContext dbContext)
    : IAiConversationRepository
{
    private const long ConversationExecutionLockNamespace = 0x4245455859414934;

    public void Add(AiConversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        dbContext.AiConversations.Add(conversation);
    }

    public void Add(AiMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        dbContext.AiMessages.Add(message);
    }

    public void Add(AiAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        dbContext.AiAnalysisRequests.Add(request);
    }

    public Task<AiConversation?> FindOwnedAsync(
        EntityId conversationId,
        EntityId accountId,
        bool includeDeleted,
        CancellationToken cancellationToken = default) =>
        dbContext.AiConversations.SingleOrDefaultAsync(
            conversation =>
                conversation.Id == conversationId &&
                conversation.AccountId == accountId &&
                (includeDeleted || conversation.DeletedAt == null),
            cancellationToken);

    public Task<bool> CursorExistsAsync(
        AiConversationPageCursor cursor,
        CancellationToken cancellationToken = default) =>
        dbContext.AiConversations.AsNoTracking().AnyAsync(
            conversation =>
                conversation.Id == cursor.ConversationId &&
                conversation.AccountId == cursor.AccountId &&
                conversation.CreatedAt == cursor.CreatedAt &&
                conversation.DeletedAt == null,
            cancellationToken);

    public async Task<IReadOnlyList<AiConversationSummary>> ListAsync(
        EntityId accountId,
        AiConversationPageCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        var query = after is null
            ? dbContext.AiConversations.FromSqlInterpolated($"""
                SELECT conversation.*
                FROM ai.ai_conversations AS conversation
                WHERE conversation.account_id = {accountId.Value}
                  AND conversation.deleted_at IS NULL
                ORDER BY conversation.created_at DESC, conversation.id DESC
                LIMIT {take}
                """)
            : dbContext.AiConversations.FromSqlInterpolated($"""
                SELECT conversation.*
                FROM ai.ai_conversations AS conversation
                WHERE conversation.account_id = {accountId.Value}
                  AND conversation.deleted_at IS NULL
                  AND (
                    conversation.created_at < {after.CreatedAt}
                    OR (
                      conversation.created_at = {after.CreatedAt}
                      AND conversation.id < {after.ConversationId.Value}
                    )
                  )
                ORDER BY conversation.created_at DESC, conversation.id DESC
                LIMIT {take}
                """);
        return await query.AsNoTracking()
            .OrderByDescending(conversation => conversation.CreatedAt)
            .ThenByDescending(conversation => conversation.Id)
            .Select(conversation => new AiConversationSummary(
                conversation.Id,
                conversation.PatientProfileId,
                conversation.CreatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiConversationMessageView>> ListMessagesAsync(
        EntityId conversationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.AiMessages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId)
            .OrderBy(message => message.Sequence)
            .Select(message => new AiConversationMessageView(
                message.Id,
                message.Role,
                message.Content,
                message.Sequence,
                message.CreatedAt))
            .ToArrayAsync(cancellationToken);

    public async Task<IAiConversationExecutionLease?> TryAcquireExecutionLeaseAsync(
        EntityId conversationId,
        CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var lockKey = GetLockKey(conversationId.Value);
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

        return new PostgreSqlExecutionLease(connection, lockKey, openedHere);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private static long GetLockKey(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes[..8]) ^
            BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]) ^
            ConversationExecutionLockNamespace;
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
        bool closeConnection) : IAiConversationExecutionLease
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
}
