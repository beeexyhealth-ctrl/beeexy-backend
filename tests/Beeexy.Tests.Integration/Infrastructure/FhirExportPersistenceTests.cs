using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Interoperability;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class FhirExportPersistenceTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task ValidatedExportAndResult_PersistExactImmutableMetadata()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        var export = CreatePendingExport(graph.HistoryEvent, EntityId.New());
        export.MarkGenerated(CreateArtifact(export, 'a'), Utc(18));
        var validation = export.RecordValidation(
            FhirValidationOutcome.Passed,
            FhirValidatorMetadata.Create("foundation-test-validator", "v-test"),
            errorCount: 0,
            warningCount: 2,
            validationCompletedAt: Utc(19));

        await using (var dbContext = CreateDbContext())
        {
            AddGraph(dbContext, graph);
            dbContext.AddRange(export, validation);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            var savedExport = await dbContext.FhirExports
                .AsNoTracking()
                .SingleAsync(value => value.Id == export.Id);
            var savedResult = await dbContext.FhirValidationResults
                .AsNoTracking()
                .SingleAsync(value => value.FhirExportId == export.Id);

            Assert.Equal(graph.Patient.Id, savedExport.PatientProfileId);
            Assert.Equal(graph.HistoryEvent.Id,
                savedExport.SourceClinicalHistoryEventId);
            Assert.Equal(export.Versions, savedExport.Versions);
            Assert.Equal(export.Artifact, savedExport.Artifact);
            Assert.Equal(FhirExportStatus.Validated, savedExport.Status);
            Assert.Equal(FhirValidationOutcome.Passed, savedExport.ValidationOutcome);
            Assert.Equal(export.ValidationCompletedAt, savedResult.ValidatedAt);
            Assert.Equal(export.Checksum, savedResult.ArtifactChecksum);
            Assert.Equal(0, savedResult.ErrorCount);
            Assert.Equal(2, savedResult.WarningCount);
        }
    }

    [Fact]
    public async Task Idempotency_IsUniquePerPatientAndIsolatedAcrossPatients()
    {
        await EnsureMigratedAsync();
        var firstGraph = CreateGraph();
        var secondGraph = CreateGraph();
        var sharedKey = EntityId.New();
        var first = CreatePendingExport(firstGraph.HistoryEvent, sharedKey);
        var differentRequest = CreatePendingExport(
            firstGraph.HistoryEvent,
            EntityId.New());
        var otherPatient = CreatePendingExport(secondGraph.HistoryEvent, sharedKey);

        await using (var dbContext = CreateDbContext())
        {
            AddGraph(dbContext, firstGraph);
            AddGraph(dbContext, secondGraph);
            dbContext.AddRange(first, differentRequest, otherPatient);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            var duplicate = CreatePendingExport(firstGraph.HistoryEvent, sharedKey);
            dbContext.FhirExports.Add(duplicate);
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                dbContext.SaveChangesAsync());
            var postgresException = Assert.IsType<PostgresException>(
                exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
            Assert.Equal(
                "ux_fhir_exports_patient_idempotency_key",
                postgresException.ConstraintName);
        }

        await using var verify = CreateDbContext();
        Assert.Equal(2, await verify.FhirExports.CountAsync(value =>
            value.PatientProfileId == firstGraph.Patient.Id));
        Assert.Single(await verify.FhirExports.Where(value =>
            value.PatientProfileId == secondGraph.Patient.Id).ToListAsync());
    }

    [Fact]
    public async Task PatientAndClinicalSourceForeignKeys_RejectMismatchAndRestrictDeletion()
    {
        await EnsureMigratedAsync();
        var sourceGraph = CreateGraph();
        var otherGraph = CreateGraph();
        await using (var dbContext = CreateDbContext())
        {
            AddGraph(dbContext, sourceGraph);
            AddGraph(dbContext, otherGraph);
            await dbContext.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO interoperability.fhir_exports " +
                "(id, patient_profile_id, source_clinical_history_event_id, " +
                "fhir_version, mapping_version, status, idempotency_key, " +
                "created_at, updated_at) VALUES " +
                "(@id, @patient, @source, 'future-release', 'mapping-v1', " +
                "'pending', @key, @created, @created);";
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("patient", otherGraph.Patient.Id.Value);
            command.Parameters.AddWithValue("source", sourceGraph.HistoryEvent.Id.Value);
            command.Parameters.AddWithValue("key", Guid.NewGuid());
            command.Parameters.AddWithValue("created", Utc(17));

            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
            Assert.Equal(
                "fk_fhir_exports_source_history_event_patient",
                exception.ConstraintName);
        }

        var export = CreatePendingExport(sourceGraph.HistoryEvent, EntityId.New());
        await using (var dbContext = CreateDbContext())
        {
            dbContext.FhirExports.Add(export);
            await dbContext.SaveChangesAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "DELETE FROM history.clinical_history_events WHERE id = @id;";
            command.Parameters.AddWithValue("id", sourceGraph.HistoryEvent.Id.Value);
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        }
    }

    [Fact]
    public async Task PostgreSql_RejectsInvalidValidatedStateOrMismatchedValidationProof()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        var export = CreatePendingExport(graph.HistoryEvent, EntityId.New());
        export.MarkGenerated(CreateArtifact(export, 'c'), Utc(18));
        var result = export.RecordValidation(
            FhirValidationOutcome.Passed,
            FhirValidatorMetadata.Create("foundation-test-validator", "v-test"),
            errorCount: 0,
            warningCount: 0,
            validationCompletedAt: Utc(19));
        await using (var dbContext = CreateDbContext())
        {
            AddGraph(dbContext, graph);
            dbContext.AddRange(export, result);
            await dbContext.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using (var invalidState = connection.CreateCommand())
        {
            invalidState.CommandText =
                "UPDATE interoperability.fhir_exports SET " +
                "status = 'validated', validation_outcome = 'failed' " +
                "WHERE id = @id;";
            invalidState.Parameters.AddWithValue("id", export.Id.Value);
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                invalidState.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal("ck_fhir_exports_lifecycle_metadata", exception.ConstraintName);
        }

        await using (var mismatchedProof = connection.CreateCommand())
        {
            mismatchedProof.CommandText =
                "UPDATE interoperability.fhir_validation_results SET " +
                "artifact_checksum = @checksum WHERE id = @id;";
            mismatchedProof.Parameters.AddWithValue("checksum", new string('d', 64));
            mismatchedProof.Parameters.AddWithValue("id", result.Id.Value);
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                mismatchedProof.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
            Assert.Equal(
                "fk_fhir_validation_results_validated_artifact",
                exception.ConstraintName);
        }
    }

    [Fact]
    public async Task Migration_CreatesOnlyFoundationTablesWithRequiredIndexesAndRestrictedFks()
    {
        await EnsureMigratedAsync();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        await using (var tablesCommand = connection.CreateCommand())
        {
            tablesCommand.CommandText =
                "SELECT table_name FROM information_schema.tables " +
                "WHERE table_schema = 'interoperability' ORDER BY table_name;";
            var tables = new List<string>();
            await using var reader = await tablesCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            Assert.Equal(["fhir_exports", "fhir_validation_results"], tables);
        }

        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText =
                "SELECT indexname FROM pg_indexes WHERE schemaname = 'interoperability' " +
                "AND indexname IN ('ux_fhir_exports_patient_idempotency_key', " +
                "'ix_fhir_exports_patient_created_id', " +
                "'ix_fhir_exports_status_updated_at', " +
                "'ix_fhir_exports_source_history_event_patient') ORDER BY indexname;";
            var indexes = new List<string>();
            await using var reader = await indexCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indexes.Add(reader.GetString(0));
            }

            Assert.Equal(4, indexes.Count);
        }

        await using var foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText =
            "SELECT tc.constraint_name, rc.delete_rule " +
            "FROM information_schema.table_constraints tc " +
            "JOIN information_schema.referential_constraints rc " +
            "ON rc.constraint_schema = tc.constraint_schema " +
            "AND rc.constraint_name = tc.constraint_name " +
            "WHERE tc.constraint_type = 'FOREIGN KEY' " +
            "AND tc.table_schema = 'interoperability' ORDER BY tc.constraint_name;";
        var foreignKeys = new List<(string Name, string DeleteRule)>();
        await using var foreignKeyReader = await foreignKeyCommand.ExecuteReaderAsync();
        while (await foreignKeyReader.ReadAsync())
        {
            foreignKeys.Add((foreignKeyReader.GetString(0), foreignKeyReader.GetString(1)));
        }

        Assert.Equal(4, foreignKeys.Count);
        Assert.All(foreignKeys, value => Assert.Equal("RESTRICT", value.DeleteRule));
    }

    private static FhirExport CreatePendingExport(
        ClinicalHistoryEvent source,
        EntityId idempotencyKey)
    {
        return FhirExport.CreatePending(
            source,
            FhirExportVersionMetadata.Create(
                "future-release-defined-by-mapping",
                "beeexy-map-2026-08-24",
                "https://profiles.example.test/beeexy-export",
                "2026.08"),
            idempotencyKey,
            Utc(17));
    }

    private static FhirArtifactMetadata CreateArtifact(FhirExport export, char value)
    {
        return FhirArtifactMetadata.Create(
            "SHA-256",
            new string(value, 64),
            $"s3://private-beeexy/fhir/{export.Id}.json");
    }

    private static void AddGraph(BeeexyDbContext dbContext, ExportGraph graph)
    {
        dbContext.AddRange(
            graph.Patient,
            graph.Questionnaire,
            graph.RuleSet,
            graph.Session,
            graph.Episode,
            graph.HistoryEvent);
    }

    private static ExportGraph CreateGraph()
    {
        var patient = PatientProfile.Create(
            BeeexyId.Create($"BXY-FHIR-{Guid.NewGuid():N}"),
            Utc(12));
        var questionnaire = QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create($"fhir-test-{Guid.NewGuid():N}"),
            DefinitionVersion.Create("test-version"),
            DefinitionHash.FromHash(new string('a', 64)),
            Utc(12),
            Utc(12));
        var ruleSet = ClinicalRuleSetVersion.ImportApproved(
            RuleSetCode.Create($"fhir-test-{Guid.NewGuid():N}"),
            DefinitionVersion.Create("test-version"),
            DefinitionHash.FromHash(new string('b', 64)),
            Utc(12),
            Utc(12));
        var session = PreTriageSession.CreateForPatient(
            patient.Id,
            questionnaire.Id,
            Utc(20),
            Utc(13));
        var episode = PreTriageEpisode.CreateFrom(session, ruleSet.Id, Utc(14));
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            episode,
            Utc(15));
        return new ExportGraph(
            patient,
            questionnaire,
            ruleSet,
            session,
            episode,
            historyEvent);
    }

    private BeeexyDbContext CreateDbContext()
    {
        return new BeeexyDbContext(
            new DbContextOptionsBuilder<BeeexyDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options);
    }

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(2026, 8, 24, hour, 0, 0, TimeSpan.Zero);
    }

    private sealed record ExportGraph(
        PatientProfile Patient,
        QuestionnaireDefinitionVersion Questionnaire,
        ClinicalRuleSetVersion RuleSet,
        PreTriageSession Session,
        PreTriageEpisode Episode,
        ClinicalHistoryEvent HistoryEvent);
}
