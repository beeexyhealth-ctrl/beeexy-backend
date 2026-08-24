using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class ClinicalHistoryPersistenceTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task EventAndAmendment_PersistImmutableReferencesWithoutClinicalPayload()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        var amendment = ClinicalAmendment.Create(
            graph.HistoryEvent,
            graph.Author.Id,
            AmendmentReason.Create("Correct patient-reported duration"),
            graph.HistoryEvent.RecordedAt.AddMinutes(1));

        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(
                graph.Author,
                graph.Patient,
                graph.Questionnaire,
                graph.RuleSet,
                graph.Session,
                graph.Episode,
                graph.HistoryEvent,
                amendment);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            var savedEvent = await dbContext.ClinicalHistoryEvents
                .AsNoTracking()
                .SingleAsync(value => value.Id == graph.HistoryEvent.Id);
            var savedAmendment = await dbContext.ClinicalAmendments
                .AsNoTracking()
                .SingleAsync(value => value.Id == amendment.Id);

            Assert.Equal(graph.Patient.Id, savedEvent.PatientProfileId);
            Assert.Equal(graph.Episode.Id, savedEvent.SourceId);
            Assert.Equal(graph.Questionnaire.Id,
                savedEvent.SourceQuestionnaireVersionId);
            Assert.Equal(graph.RuleSet.Id,
                savedEvent.SourceClinicalRuleSetVersionId);
            Assert.Equal(savedEvent.Id, savedAmendment.ClinicalHistoryEventId);
            Assert.Equal(savedEvent.SourceReference, savedAmendment.SourceReference);
            Assert.Equal(savedEvent.SourceProvenance, savedAmendment.SourceProvenance);
            Assert.Equal(graph.Author.Id, savedAmendment.AuthorAccountId);
            Assert.Equal("Correct patient-reported duration", savedAmendment.Reason.Value);
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = 'history' " +
            "AND table_name IN ('clinical_history_events', 'clinical_amendments') " +
            "AND (column_name ILIKE '%json%' OR column_name ILIKE '%payload%' " +
            "OR column_name ILIKE '%result%');";
        Assert.Null(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task PostgreSql_RejectsDuplicateProjectionOfAuthoritativeSource()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        var duplicate = ClinicalHistoryEvent.CreateCompletedPreTriage(
            graph.Episode,
            graph.HistoryEvent.RecordedAt.AddMinutes(1));

        await SaveGraphAsync(graph);

        await using var dbContext = CreateDbContext();
        dbContext.ClinicalHistoryEvents.Add(duplicate);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(
            "ux_clinical_history_events_source_projection",
            postgresException.ConstraintName);
    }

    [Fact]
    public async Task PostgreSql_RejectsPatientAndSourceFromDifferentPatients()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        var otherPatient = CreatePatient();
        await SaveGraphAsync(graph, includeHistoryEvent: false, otherPatient);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO history.clinical_history_events " +
            "(id, patient_profile_id, event_type, source_type, source_id, " +
            "source_questionnaire_version_id, source_clinical_rule_set_version_id, " +
            "occurred_at, recorded_at) VALUES " +
            "(@id, @patient, 'completed_pre_triage', 'pre_triage_episode', @source, " +
            "@questionnaire, @ruleSet, @occurred, @recorded);";
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("patient", otherPatient.Id.Value);
        command.Parameters.AddWithValue("source", graph.Episode.Id.Value);
        command.Parameters.AddWithValue("questionnaire", graph.Questionnaire.Id.Value);
        command.Parameters.AddWithValue("ruleSet", graph.RuleSet.Id.Value);
        command.Parameters.AddWithValue("occurred", graph.Episode.CompletedAt);
        command.Parameters.AddWithValue("recorded", graph.HistoryEvent.RecordedAt);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal(
            "fk_clinical_history_events_source_patient",
            exception.ConstraintName);
    }

    [Fact]
    public async Task SourceEventAndAmendmentDeletionRelationships_AreRestricted()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        var amendment = ClinicalAmendment.Create(
            graph.HistoryEvent,
            graph.Author.Id,
            AmendmentReason.Create("Traceable correction"),
            graph.HistoryEvent.RecordedAt.AddMinutes(1));

        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(
                graph.Author,
                graph.Patient,
                graph.Questionnaire,
                graph.RuleSet,
                graph.Session,
                graph.Episode,
                graph.HistoryEvent,
                amendment);
            await dbContext.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(
            PostgresErrorCodes.ForeignKeyViolation,
            (await Assert.ThrowsAsync<PostgresException>(() => ExecuteDeleteAsync(
                connection,
                "triage.pre_triage_episodes",
                graph.Episode.Id.Value))).SqlState);
        Assert.Equal(
            PostgresErrorCodes.ForeignKeyViolation,
            (await Assert.ThrowsAsync<PostgresException>(() => ExecuteDeleteAsync(
                connection,
                "history.clinical_history_events",
                graph.HistoryEvent.Id.Value))).SqlState);
        Assert.Equal(
            PostgresErrorCodes.ForeignKeyViolation,
            (await Assert.ThrowsAsync<PostgresException>(() => ExecuteDeleteAsync(
                connection,
                "identity.accounts",
                graph.Author.Id.Value))).SqlState);
    }

    [Fact]
    public async Task MoreThanTenEvents_CanBePersistedForOnePatient()
    {
        await EnsureMigratedAsync();
        var now = UtcNow();
        var patient = CreatePatient();
        var questionnaire = CreateQuestionnaire();
        var ruleSet = CreateRuleSet();
        var sessions = new List<PreTriageSession>();
        var episodes = new List<PreTriageEpisode>();
        var historyEvents = new List<ClinicalHistoryEvent>();

        for (var index = 0; index < 11; index++)
        {
            var session = PreTriageSession.CreateForPatient(
                patient.Id,
                questionnaire.Id,
                now.AddHours(8),
                now.AddMinutes(index));
            var episode = PreTriageEpisode.CreateFrom(
                session,
                ruleSet.Id,
                now.AddHours(1).AddMinutes(index));
            sessions.Add(session);
            episodes.Add(episode);
            historyEvents.Add(ClinicalHistoryEvent.CreateCompletedPreTriage(
                episode,
                episode.CompletedAt.AddMinutes(1)));
        }

        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(patient, questionnaire, ruleSet);
            dbContext.AddRange(sessions);
            dbContext.AddRange(episodes);
            dbContext.AddRange(historyEvents);
            await dbContext.SaveChangesAsync();
        }

        await using var verify = CreateDbContext();
        Assert.Equal(11, await verify.ClinicalHistoryEvents.CountAsync(
            value => value.PatientProfileId == patient.Id));
    }

    [Fact]
    public async Task Migration_CreatesRequiredHistoryIndexesConstraintsAndRestrictedForeignKeys()
    {
        await EnsureMigratedAsync();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText =
                "SELECT indexname, indexdef FROM pg_indexes " +
                "WHERE schemaname = 'history' AND indexname IN " +
                "('ux_clinical_history_events_source_projection', " +
                "'ux_clinical_amendments_event_idempotency_key', " +
                "'ix_clinical_history_events_patient_occurred_id', " +
                "'ix_clinical_history_events_patient_event_type') " +
                "ORDER BY indexname;";
            var indexes = new Dictionary<string, string>();
            await using var reader = await indexCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indexes.Add(reader.GetString(0), reader.GetString(1));
            }

            Assert.Equal(4, indexes.Count);
            Assert.Contains("UNIQUE", indexes[
                "ux_clinical_amendments_event_idempotency_key"]);
            Assert.Contains("idempotency_key IS NOT NULL", indexes[
                "ux_clinical_amendments_event_idempotency_key"]);
            Assert.Contains("UNIQUE", indexes[
                "ux_clinical_history_events_source_projection"]);
            Assert.Contains("occurred_at DESC", indexes[
                "ix_clinical_history_events_patient_occurred_id"]);
            Assert.Contains("id DESC", indexes[
                "ix_clinical_history_events_patient_occurred_id"]);
        }

        await using var constraintCommand = connection.CreateCommand();
        constraintCommand.CommandText =
            "SELECT tc.constraint_name, rc.delete_rule " +
            "FROM information_schema.table_constraints tc " +
            "JOIN information_schema.referential_constraints rc " +
            "ON rc.constraint_catalog = tc.constraint_catalog " +
            "AND rc.constraint_schema = tc.constraint_schema " +
            "AND rc.constraint_name = tc.constraint_name " +
            "WHERE tc.constraint_type = 'FOREIGN KEY' " +
            "AND tc.table_schema = 'history' ORDER BY tc.constraint_name;";
        var foreignKeys = new List<(string Name, string DeleteRule)>();
        await using var constraintReader = await constraintCommand.ExecuteReaderAsync();
        while (await constraintReader.ReadAsync())
        {
            foreignKeys.Add((constraintReader.GetString(0), constraintReader.GetString(1)));
        }

        Assert.Equal(9, foreignKeys.Count);
        Assert.All(foreignKeys, foreignKey => Assert.Equal("RESTRICT", foreignKey.DeleteRule));
        Assert.Contains(foreignKeys, value =>
            value.Name == "fk_clinical_history_events_source_patient");
        Assert.Contains(foreignKeys, value =>
            value.Name == "fk_clinical_amendments_event_source_provenance");
    }

    private async Task SaveGraphAsync(
        HistoryGraph graph,
        bool includeHistoryEvent = true,
        PatientProfile? otherPatient = null)
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(
            graph.Author,
            graph.Patient,
            graph.Questionnaire,
            graph.RuleSet,
            graph.Session,
            graph.Episode);
        if (includeHistoryEvent)
        {
            dbContext.Add(graph.HistoryEvent);
        }

        if (otherPatient is not null)
        {
            dbContext.Add(otherPatient);
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task<int> ExecuteDeleteAsync(
        NpgsqlConnection connection,
        string table,
        Guid id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE id = @id;";
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync();
    }

    private HistoryGraph CreateGraph()
    {
        var now = UtcNow();
        var author = Account.Create(
            NormalizedEmail.Create($"history-{Guid.NewGuid():N}@example.test"),
            now);
        var patient = CreatePatient();
        var questionnaire = CreateQuestionnaire();
        var ruleSet = CreateRuleSet();
        var session = PreTriageSession.CreateForPatient(
            patient.Id,
            questionnaire.Id,
            now.AddHours(8),
            now);
        var episode = PreTriageEpisode.CreateFrom(
            session,
            ruleSet.Id,
            now.AddHours(1));
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            episode,
            now.AddHours(1).AddMinutes(1));
        return new HistoryGraph(
            author,
            patient,
            questionnaire,
            ruleSet,
            session,
            episode,
            historyEvent);
    }

    private static PatientProfile CreatePatient()
    {
        return PatientProfile.Create(
            BeeexyId.Create($"BXY-HISTORY-{Guid.NewGuid():N}"),
            UtcNow());
    }

    private static QuestionnaireDefinitionVersion CreateQuestionnaire()
    {
        return QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create($"history-test-{Guid.NewGuid():N}"),
            DefinitionVersion.Create("test-version"),
            DefinitionHash.FromHash(new string('a', 64)),
            UtcNow(),
            UtcNow());
    }

    private static ClinicalRuleSetVersion CreateRuleSet()
    {
        return ClinicalRuleSetVersion.ImportApproved(
            RuleSetCode.Create($"history-test-{Guid.NewGuid():N}"),
            DefinitionVersion.Create("test-version"),
            DefinitionHash.FromHash(new string('b', 64)),
            UtcNow(),
            UtcNow());
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

    private static DateTimeOffset UtcNow()
    {
        return new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed record HistoryGraph(
        Account Author,
        PatientProfile Patient,
        QuestionnaireDefinitionVersion Questionnaire,
        ClinicalRuleSetVersion RuleSet,
        PreTriageSession Session,
        PreTriageEpisode Episode,
        ClinicalHistoryEvent HistoryEvent);
}
