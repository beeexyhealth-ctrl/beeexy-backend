using System.Security.Cryptography;
using System.Text;
using System.Data;
using Beeexy.Application.Identity;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Identity;

public sealed class PostgreSqlPrivateAccessRateLimiter(
    BeeexyDbContext dbContext,
    PrivateAccessRateLimitPolicy policy) : IPrivateAccessRateLimiter
{
    public async Task<PrivateAccessRateLimitDecision> TryAcquireAsync(
        string requesterIpAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterIpAddress);
        var keyHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(requesterIpAddress)));
        var windowEndsAt = now.Add(policy.Window);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
                """
                INSERT INTO identity.private_access_login_windows
                    (key_hash, attempt_count, window_ends_at, updated_at)
                VALUES (@keyHash, 1, @windowEndsAt, @now)
                ON CONFLICT (key_hash) DO UPDATE SET
                    attempt_count = CASE
                        WHEN private_access_login_windows.window_ends_at <= @now THEN 1
                        ELSE private_access_login_windows.attempt_count + 1
                    END,
                    window_ends_at = CASE
                        WHEN private_access_login_windows.window_ends_at <= @now THEN @windowEndsAt
                        ELSE private_access_login_windows.window_ends_at
                    END,
                    updated_at = @now
                RETURNING attempt_count, window_ends_at
                """;
        AddParameter(command, "keyHash", DbType.String, keyHash);
        AddParameter(command, "windowEndsAt", DbType.DateTimeOffset, windowEndsAt);
        AddParameter(command, "now", DbType.DateTimeOffset, now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The private-access rate limit could not be acquired.");
        }

        var attemptCount = reader.GetInt32(0);
        var persistedWindowEndsAt = reader.GetFieldValue<DateTimeOffset>(1);
        await reader.DisposeAsync();

        if (Random.Shared.Next(256) == 0)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM identity.private_access_login_windows WHERE window_ends_at < {now.AddDays(-1)}",
                cancellationToken);
        }

        return attemptCount <= policy.PermitLimit
            ? PrivateAccessRateLimitDecision.Allowed
            : PrivateAccessRateLimitDecision.Rejected(persistedWindowEndsAt - now);
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        DbType type,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

public sealed record PrivateAccessRateLimitPolicy(int PermitLimit, TimeSpan Window);
