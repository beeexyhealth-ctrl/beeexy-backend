using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class MigrationTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task AllMigrations_ApplyToFreshPostgreSqlWithPhase21Tables()
    {
        var options = new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options;

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();

            var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();

            Assert.Equal(
                [
                    "20260819193818_InitialFoundation",
                    "20260819214410_Phase21IdentityPersistenceFoundation",
                    "20260820015208_Phase24RefreshSessionRotation",
                    "20260820053544_Phase26ProfileOptimisticConcurrency"
                ],
                appliedMigrations);
            Assert.Empty(pendingMigrations);
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT table_schema || '.' || table_name " +
            "FROM information_schema.tables " +
            "WHERE table_type = 'BASE TABLE' " +
            "AND table_schema NOT IN ('pg_catalog', 'information_schema') " +
            "ORDER BY 1;";

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(
            [
                "identity.accounts",
                "identity.email_authentication_challenges",
                "identity.external_identities",
                "identity.refresh_sessions",
                "patients.patient_profiles",
                "patients.user_preferences",
                "public.__EFMigrationsHistory"
            ],
            tables);
    }
}
