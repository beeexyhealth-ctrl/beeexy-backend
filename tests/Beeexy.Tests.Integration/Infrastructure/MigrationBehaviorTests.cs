using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class MigrationBehaviorTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Phase21Migration_CreatesRequiredPartialIndexes()
    {
        await EnsureMigratedAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT indexname, indexdef FROM pg_indexes " +
            "WHERE indexname IN " +
            "('ix_refresh_sessions_active_account_expiry', " +
            "'ix_email_authentication_challenges_pending_expiry') " +
            "ORDER BY indexname;";

        var indexes = new Dictionary<string, string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0), reader.GetString(1));
        }

        Assert.Equal(2, indexes.Count);
        var challengeIndex = indexes["ix_email_authentication_challenges_pending_expiry"];
        Assert.Contains("WHERE", challengeIndex);
        Assert.Contains("pending", challengeIndex);
        Assert.Contains("consumed_at", challengeIndex);
        Assert.Contains("IS NULL", challengeIndex);

        var refreshIndex = indexes["ix_refresh_sessions_active_account_expiry"];
        Assert.Contains("WHERE", refreshIndex);
        Assert.Contains("active", refreshIndex);
    }

    [Fact]
    public async Task Phase21Migration_CanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260819193818_InitialFoundation");
            var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
            Assert.Equal(["20260819193818_InitialFoundation"], appliedMigrations);
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema IN ('identity', 'patients');";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        }
    }

    private DbContextOptions<BeeexyDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options;
    }

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = new BeeexyDbContext(CreateOptions());
        await dbContext.Database.MigrateAsync();
    }
}
