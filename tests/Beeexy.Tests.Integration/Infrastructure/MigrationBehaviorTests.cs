using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class MigrationBehaviorTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Phase47Migration_MakesOnlyUrgencyNullableAndCanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        Assert.Equal("YES", await LoadUrgencyNullabilityAsync());
        var options = CreateOptions();

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260822061610_Phase45ConfirmedDemoPackages");
        }

        Assert.Equal("NO", await LoadUrgencyNullabilityAsync());
        var now = DateTimeOffset.UtcNow;
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        ClinicalAssessment historicalAssessment;
        await using (var dbContext = new BeeexyDbContext(options))
        {
            var importer = new ClinicalDefinitionImporter(
                dbContext,
                new ClinicalDefinitionPackageValidator(),
                NullLogger<ClinicalDefinitionImporter>.Instance);
            await importer.ImportAsync(package);
            var session = PreTriageSession.CreateAnonymous(
                package.Questionnaire.Id,
                AnonymousCapabilityHash.FromHash(Guid.NewGuid().ToString("N")),
                now.AddHours(24),
                now);
            var episode = PreTriageEpisode.CreateFrom(
                session,
                package.RuleSet.Id,
                now.AddMinutes(1),
                now.AddHours(24));
            historicalAssessment = ClinicalAssessment.Create(
                episode,
                UrgencyCode.Create("historical-test-urgency"),
                episode.CompletedAt);
            dbContext.AddRange(session, episode, historicalAssessment);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
            var preserved = await dbContext.ClinicalAssessments
                .AsNoTracking()
                .SingleAsync(value => value.Id == historicalAssessment.Id);
            Assert.Equal("historical-test-urgency", preserved.UrgencyCode!.Value);
        }

        Assert.Equal("YES", await LoadUrgencyNullabilityAsync());
    }

    [Fact]
    public async Task Phase45Migration_PreservesDemoDefinitionsAcrossRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();
        await using (var dbContext = new BeeexyDbContext(options))
        {
            var importer = new ClinicalDefinitionImporter(
                dbContext,
                new ClinicalDefinitionPackageValidator(),
                NullLogger<ClinicalDefinitionImporter>.Instance);
            foreach (var package in SimplifiedDemoDefinitionPackages.CreateAll())
            {
                await importer.ImportAsync(package);
            }
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260822035009_Phase42ClinicalDefinitionPackages");
        }

        Assert.Equal("REFERENCE_PLATFORM_DERIVED", await LoadDemoSourceAsync());

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        }

        Assert.Equal("PRODUCT_DEMO_DEFINED", await LoadDemoSourceAsync());
    }

    [Fact]
    public async Task Phase42Migration_CanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260821203135_Phase41PreTriagePersistenceFoundation");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.columns " +
                "WHERE table_schema = 'triage' " +
                "AND table_name IN ('questionnaire_versions', 'clinical_rule_set_versions') " +
                "AND column_name IN ('pathway_code', 'clinical_content_source', " +
                "'clinical_review_status', 'clinical_approval_status', " +
                "'definition_metadata');";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        }
    }

    [Fact]
    public async Task Phase41Migration_CanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260821065021_Phase36ApprovedPatientDemographics");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema = 'triage';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        }
    }

    [Fact]
    public async Task Phase36Migration_PreservesLegacyProfileAndCanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();
        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260821015511_Phase31CareRelationshipFoundation");
        }

        var profileId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO patients.patient_profiles " +
                "(id, account_id, beeexy_id, created_at, updated_at) " +
                "VALUES (@id, NULL, @beeexyId, @createdAt, NULL);";
            insert.Parameters.AddWithValue("id", profileId);
            insert.Parameters.AddWithValue("beeexyId", $"BXY-{profileId:N}".ToUpperInvariant());
            insert.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT first_name, last_name, date_of_birth, " +
                "sex_assigned_at_birth, state, version " +
                "FROM patients.patient_profiles WHERE id = @id;";
            command.Parameters.AddWithValue("id", profileId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.IsDBNull(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
            Assert.True(reader.IsDBNull(3));
            Assert.True(reader.IsDBNull(4));
            Assert.Equal(1L, reader.GetInt64(5));
        }
    }

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
    public async Task Phase24Migration_CreatesRefreshRotationLineageSchema()
    {
        await EnsureMigratedAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        await using (var columnCommand = connection.CreateCommand())
        {
            columnCommand.CommandText =
                "SELECT column_name, is_nullable FROM information_schema.columns " +
                "WHERE table_schema = 'identity' AND table_name = 'refresh_sessions' " +
                "AND column_name IN ('family_id', 'parent_session_id', " +
                "'replaced_by_session_id', 'rotated_at') ORDER BY column_name;";

            var columns = new Dictionary<string, string>();
            await using var reader = await columnCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0), reader.GetString(1));
            }

            Assert.Equal(4, columns.Count);
            Assert.Equal("NO", columns["family_id"]);
            Assert.Equal("YES", columns["parent_session_id"]);
            Assert.Equal("YES", columns["replaced_by_session_id"]);
            Assert.Equal("YES", columns["rotated_at"]);
        }

        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText =
                "SELECT indexname, indexdef FROM pg_indexes " +
                "WHERE schemaname = 'identity' AND tablename = 'refresh_sessions' " +
                "AND indexname IN ('ix_refresh_sessions_family_id', " +
                "'ux_refresh_sessions_parent_session_id');";

            var indexes = new Dictionary<string, string>();
            await using var reader = await indexCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indexes.Add(reader.GetString(0), reader.GetString(1));
            }

            Assert.Equal(2, indexes.Count);
            Assert.Contains("UNIQUE", indexes["ux_refresh_sessions_parent_session_id"]);
            Assert.Contains("WHERE", indexes["ux_refresh_sessions_parent_session_id"]);
        }

        await using var constraintCommand = connection.CreateCommand();
        constraintCommand.CommandText =
            "SELECT count(*) FROM pg_constraint " +
            "WHERE conname = 'ck_refresh_sessions_rotation';";
        Assert.Equal(1L, (long)(await constraintCommand.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Phase26Migration_AddsPositivePreferenceConcurrencyVersion()
    {
        await EnsureMigratedAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using (var columnCommand = connection.CreateCommand())
        {
            columnCommand.CommandText =
                "SELECT data_type, is_nullable, column_default " +
                "FROM information_schema.columns " +
                "WHERE table_schema = 'patients' AND table_name = 'user_preferences' " +
                "AND column_name = 'version';";
            await using var reader = await columnCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("bigint", reader.GetString(0));
            Assert.Equal("NO", reader.GetString(1));
            Assert.Contains("1", reader.GetString(2), StringComparison.Ordinal);
        }

        await using var constraintCommand = connection.CreateCommand();
        constraintCommand.CommandText =
            "SELECT count(*) FROM pg_constraint " +
            "WHERE conname = 'ck_user_preferences_version_positive';";
        Assert.Equal(1L, (long)(await constraintCommand.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Phase31Migration_CanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260820053544_Phase26ProfileOptimisticConcurrency");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema = 'patients' AND table_name = 'care_relationships';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        }
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

    private async Task<string> LoadDemoSourceAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT clinical_content_source FROM triage.questionnaire_versions " +
            "WHERE version = @version ORDER BY pathway_code LIMIT 1;";
        command.Parameters.AddWithValue(
            "version", SimplifiedDemoDefinitionPackages.VersionIdentifier);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> LoadUrgencyNullabilityAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT is_nullable FROM information_schema.columns " +
            "WHERE table_schema = 'triage' AND table_name = 'clinical_assessments' " +
            "AND column_name = 'urgency_code';";
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
