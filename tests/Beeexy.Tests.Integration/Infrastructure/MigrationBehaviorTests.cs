using Beeexy.Application.Triage;
using Beeexy.Domain.Patients;
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
    public async Task Phase8SchedulingMigrations_AreAdditiveAndCanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();
        var markerPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-SCHEDULING-MIGRATION-{Guid.NewGuid():N}"),
            DateTimeOffset.UtcNow);
        await using (var dbContext = new BeeexyDbContext(options))
        {
            dbContext.PatientProfiles.Add(markerPatient);
            await dbContext.SaveChangesAsync();
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260829070507_Phase75VersionedDoctorMatching");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema = 'scheduling';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            Assert.NotNull(await dbContext.PatientProfiles.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == markerPatient.Id));
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
            Assert.NotNull(await dbContext.PatientProfiles.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == markerPatient.Id));
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema = 'scheduling';";
            Assert.Equal(5L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Phase72DirectoryImportMigration_IsAdditiveAndCanRollbackAndReapplyWithoutSeeds()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();
        var markerPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-DIRECTORY-IMPORT-MIGRATION-{Guid.NewGuid():N}"),
            DateTimeOffset.UtcNow);
        await using (var dbContext = new BeeexyDbContext(options))
        {
            dbContext.PatientProfiles.Add(markerPatient);
            await dbContext.SaveChangesAsync();
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260829012832_Phase71DirectoryFoundation");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema = 'directory' " +
                "AND table_name = 'demo_directory_imports';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            Assert.NotNull(await dbContext.PatientProfiles.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == markerPatient.Id));
            Assert.Equal(12, await CountDirectoryTablesAsync(dbContext));
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
            Assert.NotNull(await dbContext.PatientProfiles.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == markerPatient.Id));
            Assert.Equal(14, await CountDirectoryTablesAsync(dbContext));
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM directory.demo_directory_imports;";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Phase75DoctorMatchingMigration_IsAdditiveAndCanRollbackAndReapplyWithoutRules()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();
        var markerPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-DOCTOR-MATCH-MIGRATION-{Guid.NewGuid():N}"),
            DateTimeOffset.UtcNow);
        await using (var dbContext = new BeeexyDbContext(options))
        {
            dbContext.PatientProfiles.Add(markerPatient);
            await dbContext.SaveChangesAsync();
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260829040757_Phase72SyntheticDemoDirectoryImport");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema = 'directory' " +
                "AND table_name = 'doctor_match_rule_configurations';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            Assert.NotNull(await dbContext.PatientProfiles.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == markerPatient.Id));
            Assert.Equal(13, await CountDirectoryTablesAsync(dbContext));
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
            Assert.Equal(14, await CountDirectoryTablesAsync(dbContext));
            Assert.Empty(await dbContext.DoctorMatchRuleVersions.AsNoTracking().ToListAsync());
            Assert.Empty(await dbContext.DoctorMatchRuleConfigurations.AsNoTracking().ToListAsync());
        }
    }

    [Fact]
    public async Task Phase71DirectoryMigration_IsAdditiveAndCanRollbackAndReapplyWithoutSeeds()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();
        var markerPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-DIRECTORY-MIGRATION-{Guid.NewGuid():N}"),
            DateTimeOffset.UtcNow);
        await using (var dbContext = new BeeexyDbContext(options))
        {
            dbContext.PatientProfiles.Add(markerPatient);
            await dbContext.SaveChangesAsync();
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260828203109_DatabaseBackedPrivateAccess");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema = 'directory';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            Assert.NotNull(await dbContext.PatientProfiles.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == markerPatient.Id));
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
            Assert.NotNull(await dbContext.PatientProfiles.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == markerPatient.Id));
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT (SELECT count(*) FROM directory.clinics) + " +
                "(SELECT count(*) FROM directory.doctors) + " +
                "(SELECT count(*) FROM directory.doctor_match_rule_versions);";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    private static async Task<int> CountDirectoryTablesAsync(BeeexyDbContext dbContext)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        command.CommandText =
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'directory';";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task DatabasePrivateAccessMigration_IsAdditiveAndCanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260828040441_EducationalVideoOfferWorkflow");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema = 'identity' AND table_name LIKE 'private_access_%';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        }
    }

    [Fact]
    public async Task Part31IdempotencyMigration_IsAdditiveAndCanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260824202650_Phase61FhirExportPersistenceFoundation");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema = 'triage' " +
                "AND table_name = 'pre_triage_intake_idempotency';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        }
    }

    [Fact]
    public async Task Phase61Migration_IsAdditiveAndCanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();
        var markerPatient = PatientProfile.Create(
            BeeexyId.Create($"BXY-FHIR-MIGRATION-{Guid.NewGuid():N}"),
            DateTimeOffset.UtcNow);
        await using (var dbContext = new BeeexyDbContext(options))
        {
            dbContext.PatientProfiles.Add(markerPatient);
            await dbContext.SaveChangesAsync();
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260824035248_Phase55TraceablePreTriageAmendments");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema = 'interoperability';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            Assert.NotNull(await dbContext.PatientProfiles
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == markerPatient.Id));
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
            Assert.NotNull(await dbContext.PatientProfiles
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == markerPatient.Id));
        }
    }

    [Fact]
    public async Task Phase55Migration_IsAdditiveAndCanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260823192326_Phase51ClinicalHistoryFoundation");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.columns " +
                "WHERE table_schema = 'history' " +
                "AND table_name = 'clinical_amendments' " +
                "AND column_name = 'idempotency_key';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        }
    }

    [Fact]
    public async Task Phase51Migration_CanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260822182341_Phase410ClinicalHistoryProjectionBoundary");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema = 'history' AND table_name IN " +
                "('clinical_history_events', 'clinical_amendments');";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        }
    }

    [Fact]
    public async Task Phase410Migration_BackfillsValidPatientEpisodeAndCanRollbackAndReapply()
    {
        await EnsureMigratedAsync();
        var options = CreateOptions();
        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260822163355_Phase47NeutralClinicalAssessment");
        }
        await AddCurrentSessionColumnsForHistoricalModelAsync();

        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var now = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var patient = PatientProfile.CreateManaged(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            PatientName.Create("Migration"),
            PatientName.Create("Projection"),
            new DateOnly(1990, 1, 1),
            SexAssignedAtBirth.Female,
            UsState.Create("NY"),
            now);
        PreTriageEpisode episode;
        await using (var dbContext = new BeeexyDbContext(options))
        {
            var importer = new ClinicalDefinitionImporter(
                dbContext,
                new ClinicalDefinitionPackageValidator(),
                NullLogger<ClinicalDefinitionImporter>.Instance);
            await importer.ImportAsync(package);
            var session = PreTriageSession.CreateForPatient(
                patient.Id,
                package.Questionnaire.Id,
                now.AddHours(24),
                now);
            AddDemoAnswersAndSymptoms(session, package, now.AddMinutes(1));
            episode = PreTriageEpisode.CreateFrom(
                session,
                package.RuleSet.Id,
                now.AddMinutes(2));
            var assessment = ClinicalAssessment.CreateNeutral(episode, episode.CompletedAt);
            dbContext.AddRange(patient, session, episode, assessment);
            await dbContext.SaveChangesAsync();
        }
        await DropCurrentSessionColumnsForHistoricalModelAsync();

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            var record = await dbContext.PreTriageHistoryProjectionRecords
                .AsNoTracking()
                .SingleAsync(value => value.SourceEpisodeId == episode.Id);
            Assert.Equal(patient.Id, record.PatientProfileId);
            Assert.Equal(episode.CompletedAt, record.CompletedAt);
            Assert.Equal(episode.CompletedAt, record.CreatedAt);
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260822163355_Phase47NeutralClinicalAssessment");
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM information_schema.tables " +
                "WHERE table_schema = 'history';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        await using (var dbContext = new BeeexyDbContext(options))
        {
            await dbContext.Database.MigrateAsync();
            Assert.Equal(1, await dbContext.PreTriageHistoryProjectionRecords.CountAsync(
                value => value.SourceEpisodeId == episode.Id));
            await dbContext.PreTriageHistoryProjectionRecords
                .Where(value => value.SourceEpisodeId == episode.Id)
                .ExecuteDeleteAsync();
            await dbContext.ClinicalAssessments
                .Where(value => value.EpisodeId == episode.Id)
                .ExecuteDeleteAsync();
            await dbContext.TriageAnswers
                .Where(value => value.EpisodeId == episode.Id)
                .ExecuteDeleteAsync();
            await dbContext.ReportedSymptoms
                .Where(value => value.EpisodeId == episode.Id)
                .ExecuteDeleteAsync();
            await dbContext.PreTriageEpisodes
                .Where(value => value.Id == episode.Id)
                .ExecuteDeleteAsync();
            await dbContext.PreTriageSessions
                .Where(value => value.Id == episode.SourceSessionId)
                .ExecuteDeleteAsync();
            await dbContext.PatientProfiles
                .Where(value => value.Id == patient.Id)
                .ExecuteDeleteAsync();
        }
    }

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
        await AddCurrentSessionColumnsForHistoricalModelAsync();

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
        await DropCurrentSessionColumnsForHistoricalModelAsync();

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

    private async Task AddCurrentSessionColumnsForHistoricalModelAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "ALTER TABLE triage.pre_triage_sessions " +
            "ADD COLUMN educational_video_decision character varying(8) NULL, " +
            "ADD COLUMN educational_video_offer_required boolean NOT NULL DEFAULT FALSE, " +
            "ADD COLUMN educational_video_offer_resolved_at timestamp with time zone NULL;";
        await command.ExecuteNonQueryAsync();
    }

    private async Task DropCurrentSessionColumnsForHistoricalModelAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "ALTER TABLE triage.pre_triage_sessions " +
            "DROP COLUMN educational_video_decision, " +
            "DROP COLUMN educational_video_offer_required, " +
            "DROP COLUMN educational_video_offer_resolved_at;";
        await command.ExecuteNonQueryAsync();
    }

    private static void AddDemoAnswersAndSymptoms(
        PreTriageSession session,
        ClinicalDefinitionPackage package,
        DateTimeOffset recordedAt)
    {
        var answers = new Dictionary<string, string>
        {
            ["DURATION"] = "{\"value\":2,\"unit\":\"DAYS\"}",
            ["INTENSITY"] = "{\"value\":7}",
            ["ADDITIONAL_SYMPTOMS"] = "{\"values\":[\"FEVER\"]}"
        };
        foreach (var (code, json) in answers)
        {
            var question = package.Questionnaire.Questions.Single(
                value => value.Code == QuestionCode.Create(code));
            session.RecordAnswer(question, json, question.DisplayOrder, recordedAt);
        }

        session.ReportSymptom(
            SymptomText.Create("HEADACHE"),
            1,
            recordedAt,
            "urn:beeexy:demo-symptom-code",
            "HEADACHE",
            "Headache",
            "BEEEXY_SIMPLIFIED_DEMO_PACKAGE",
            recordedAt);
        session.ReportSymptom(
            SymptomText.Create("FEVER"),
            2,
            recordedAt,
            "urn:beeexy:demo-symptom-code",
            "FEVER",
            "FEVER",
            "BEEEXY_SIMPLIFIED_DEMO_PACKAGE",
            recordedAt);
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
