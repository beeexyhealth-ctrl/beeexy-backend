using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class PreTriagePersistenceTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task AnonymousSession_PersistsNullablePatientAndOnlyCapabilityHash()
    {
        await EnsureMigratedAsync();
        var questionnaire = CreateQuestionnaire();
        var hash = AnonymousCapabilityHash.FromHash(UniqueHash());
        var session = PreTriageSession.CreateAnonymous(
            questionnaire.Id,
            hash,
            UtcNow().AddHours(24),
            UtcNow());

        await using (var dbContext = CreateDbContext())
        {
            dbContext.QuestionnaireVersions.Add(questionnaire);
            dbContext.PreTriageSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.PreTriageSessions
                .AsNoTracking()
                .SingleAsync(value => value.Id == session.Id);
            Assert.Null(saved.PatientProfileId);
            Assert.Equal(hash, saved.AnonymousCapabilityHash);
            Assert.Equal(PreTriageSessionStatus.Active, saved.Status);
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = 'triage' AND table_name = 'pre_triage_sessions' " +
            "AND (column_name ILIKE '%token%' OR column_name ILIKE '%capability%') " +
            "ORDER BY column_name;";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Equal(["anonymous_capability_hash"], columns);
    }

    [Fact]
    public async Task AuthenticatedSession_PersistsPatientAssociationWithoutCapabilityHash()
    {
        await EnsureMigratedAsync();
        var questionnaire = CreateQuestionnaire();
        var patient = CreatePatient();
        var session = PreTriageSession.CreateForPatient(
            patient.Id,
            questionnaire.Id,
            UtcNow().AddHours(8),
            UtcNow());

        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(questionnaire, patient, session);
            await dbContext.SaveChangesAsync();
        }

        await using var verify = CreateDbContext();
        var saved = await verify.PreTriageSessions
            .AsNoTracking()
            .SingleAsync(value => value.Id == session.Id);
        Assert.Equal(patient.Id, saved.PatientProfileId);
        Assert.Null(saved.AnonymousCapabilityHash);
    }

    [Fact]
    public async Task Completion_PersistsPermanentGraphAndPromotesTemporaryRows()
    {
        await EnsureMigratedAsync();
        var graph = CreateCompletedAnonymousGraph();

        await using (var dbContext = CreateDbContext())
        {
            dbContext.QuestionnaireVersions.Add(graph.Questionnaire);
            dbContext.ClinicalRuleSetVersions.Add(graph.RuleSet);
            dbContext.PreTriageSessions.Add(graph.Session);
            dbContext.PreTriageEpisodes.Add(graph.Episode);
            dbContext.ClinicalAssessments.Add(graph.Assessment);
            await dbContext.SaveChangesAsync();
        }

        await using var verify = CreateDbContext();
        var session = await verify.PreTriageSessions
            .AsNoTracking()
            .SingleAsync(value => value.Id == graph.Session.Id);
        var episode = await verify.PreTriageEpisodes
            .AsNoTracking()
            .SingleAsync(value => value.Id == graph.Episode.Id);
        var answer = await verify.TriageAnswers
            .AsNoTracking()
            .SingleAsync(value => value.EpisodeId == episode.Id);
        var symptom = await verify.ReportedSymptoms
            .AsNoTracking()
            .SingleAsync(value => value.EpisodeId == episode.Id);
        var assessment = await verify.ClinicalAssessments
            .AsNoTracking()
            .SingleAsync(value => value.EpisodeId == episode.Id);
        var finding = await verify.ClinicalFindings
            .AsNoTracking()
            .SingleAsync(value => value.AssessmentId == assessment.Id);

        Assert.Equal(PreTriageSessionStatus.Completed, session.Status);
        Assert.Null(answer.SessionId);
        Assert.Equal(episode.Id, answer.EpisodeId);
        Assert.Null(symptom.SessionId);
        Assert.Equal(episode.Id, symptom.EpisodeId);
        Assert.Equal(graph.Questionnaire.Id, episode.QuestionnaireVersionId);
        Assert.Equal(graph.RuleSet.Id, episode.ClinicalRuleSetVersionId);
        Assert.Equal(graph.RuleSet.Id, assessment.ClinicalRuleSetVersionId);
        Assert.Equal("test-finding", finding.FindingCode);
    }

    [Fact]
    public async Task PostgreSql_EnforcesOneEpisodePerSourceSession()
    {
        await EnsureMigratedAsync();
        var graph = CreateCompletedAnonymousGraph();
        await SaveCompletedGraphAsync(graph);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO triage.pre_triage_episodes " +
            "(id, source_session_id, patient_profile_id, questionnaire_version_id, " +
            "clinical_rule_set_version_id, completed_at, anonymous_expires_at, claimed_at) " +
            "VALUES (@id, @session, NULL, @questionnaire, @rules, @completed, @expires, NULL);";
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("session", graph.Session.Id.Value);
        command.Parameters.AddWithValue("questionnaire", graph.Questionnaire.Id.Value);
        command.Parameters.AddWithValue("rules", graph.RuleSet.Id.Value);
        command.Parameters.AddWithValue("completed", graph.Episode.CompletedAt);
        command.Parameters.AddWithValue("expires", graph.Episode.AnonymousExpiresAt!.Value);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ux_pre_triage_episodes_source_session_id", exception.ConstraintName);
    }

    [Fact]
    public async Task PostgreSql_EnforcesUniqueCapabilityHashWhenPresent()
    {
        await EnsureMigratedAsync();
        var questionnaire = CreateQuestionnaire();
        var hash = AnonymousCapabilityHash.FromHash(UniqueHash());
        var first = PreTriageSession.CreateAnonymous(
            questionnaire.Id,
            hash,
            UtcNow().AddHours(24),
            UtcNow());
        var second = PreTriageSession.CreateAnonymous(
            questionnaire.Id,
            hash,
            UtcNow().AddHours(24),
            UtcNow());

        await using var dbContext = CreateDbContext();
        dbContext.AddRange(questionnaire, first, second);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(
            "ux_pre_triage_sessions_anonymous_capability_hash",
            postgresException.ConstraintName);
    }

    [Fact]
    public async Task PostgreSql_EnforcesDefinitionVersionLookupUniqueness()
    {
        await EnsureMigratedAsync();
        var first = CreateQuestionnaire("test-unique", "test-version");
        var duplicate = CreateQuestionnaire("test-unique", "test-version");

        await using var dbContext = CreateDbContext();
        dbContext.AddRange(first, duplicate);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal("ux_questionnaire_versions_code_version", postgresException.ConstraintName);
    }

    [Fact]
    public async Task ActiveSessionDeletion_RemovesOnlyTemporaryChildrenAndPreservesPatient()
    {
        await EnsureMigratedAsync();
        var patient = CreatePatient();
        var questionnaire = CreateQuestionnaire(includeQuestion: true);
        var question = Assert.Single(questionnaire.Questions);
        var session = PreTriageSession.CreateForPatient(
            patient.Id,
            questionnaire.Id,
            UtcNow().AddHours(8),
            UtcNow());
        var answer = session.RecordAnswer(question, "true", 1, UtcNow().AddMinutes(1));
        var symptom = session.ReportSymptom(
            SymptomText.Create("Test free text"),
            1,
            UtcNow().AddMinutes(1));

        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(patient, questionnaire, session);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            var savedSession = await dbContext.PreTriageSessions
                .SingleAsync(value => value.Id == session.Id);
            dbContext.PreTriageSessions.Remove(savedSession);
            await dbContext.SaveChangesAsync();
        }

        await using var verify = CreateDbContext();
        Assert.True(await verify.PatientProfiles.AnyAsync(value => value.Id == patient.Id));
        Assert.False(await verify.TriageAnswers.AnyAsync(value => value.Id == answer.Id));
        Assert.False(await verify.ReportedSymptoms.AnyAsync(value => value.Id == symptom.Id));
    }

    [Fact]
    public async Task PatientDeletion_IsRestrictedBySessionAndPermanentEpisodeReferences()
    {
        await EnsureMigratedAsync();
        var patient = CreatePatient();
        var questionnaire = CreateQuestionnaire();
        var ruleSet = CreateRuleSet();
        var session = PreTriageSession.CreateForPatient(
            patient.Id,
            questionnaire.Id,
            UtcNow().AddHours(8),
            UtcNow());
        var episode = PreTriageEpisode.CreateFrom(session, ruleSet.Id, UtcNow().AddHours(1));

        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(patient, questionnaire, ruleSet, session, episode);
            await dbContext.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM patients.patient_profiles WHERE id = @id;";
        command.Parameters.AddWithValue("id", patient.Id.Value);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task AnonymousClaim_UpdatesOwnershipWithoutRecreatingClinicalContent()
    {
        await EnsureMigratedAsync();
        var graph = CreateCompletedAnonymousGraph();
        var patient = CreatePatient();
        await SaveCompletedGraphAsync(graph, patient);
        var assessmentId = graph.Assessment.Id;
        var findingId = graph.Assessment.Findings.Single().Id;

        graph.Episode.Claim(patient.Id, graph.Episode.CompletedAt.AddMinutes(1));
        await using (var dbContext = CreateDbContext())
        {
            dbContext.PreTriageEpisodes.Update(graph.Episode);
            await dbContext.SaveChangesAsync();
        }

        await using var verify = CreateDbContext();
        var saved = await verify.PreTriageEpisodes
            .AsNoTracking()
            .SingleAsync(value => value.Id == graph.Episode.Id);
        Assert.Equal(patient.Id, saved.PatientProfileId);
        Assert.NotNull(saved.ClaimedAt);
        Assert.True(await verify.ClinicalAssessments.AnyAsync(value => value.Id == assessmentId));
        Assert.True(await verify.ClinicalFindings.AnyAsync(value => value.Id == findingId));
    }

    [Fact]
    public async Task Migration_CreatesRequiredTriageIndexesAndChecks()
    {
        await EnsureMigratedAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText =
                "SELECT indexname FROM pg_indexes WHERE schemaname = 'triage' " +
                "AND indexname IN ('ux_pre_triage_sessions_anonymous_capability_hash', " +
                "'ix_pre_triage_sessions_status_expiry', " +
                "'ux_pre_triage_episodes_source_session_id', " +
                "'ix_pre_triage_episodes_patient_completed_at', " +
                "'ix_pre_triage_episodes_unclaimed_expiry', " +
                "'ux_questionnaire_versions_code_version', " +
                "'ux_clinical_rule_set_versions_code_version') ORDER BY indexname;";
            var indexes = new List<string>();
            await using var reader = await indexCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indexes.Add(reader.GetString(0));
            }

            Assert.Equal(7, indexes.Count);
        }

        await using var constraintCommand = connection.CreateCommand();
        constraintCommand.CommandText =
            "SELECT count(*) FROM pg_constraint WHERE connamespace = 'triage'::regnamespace " +
            "AND conname IN ('ck_pre_triage_sessions_ownership', " +
            "'ck_pre_triage_sessions_completion', 'ck_pre_triage_episodes_anonymous_claim', " +
            "'ck_answers_owner', 'ck_reported_symptoms_owner');";
        Assert.Equal(5L, (long)(await constraintCommand.ExecuteScalarAsync())!);
    }

    private async Task SaveCompletedGraphAsync(
        CompletedGraph graph,
        PatientProfile? patient = null)
    {
        await using var dbContext = CreateDbContext();
        dbContext.QuestionnaireVersions.Add(graph.Questionnaire);
        dbContext.ClinicalRuleSetVersions.Add(graph.RuleSet);
        dbContext.PreTriageSessions.Add(graph.Session);
        dbContext.PreTriageEpisodes.Add(graph.Episode);
        dbContext.ClinicalAssessments.Add(graph.Assessment);
        if (patient is not null)
        {
            dbContext.PatientProfiles.Add(patient);
        }

        await dbContext.SaveChangesAsync();
    }

    private CompletedGraph CreateCompletedAnonymousGraph()
    {
        var now = UtcNow();
        var questionnaire = CreateQuestionnaire(includeQuestion: true);
        var question = Assert.Single(questionnaire.Questions);
        var ruleSet = CreateRuleSet();
        var session = PreTriageSession.CreateAnonymous(
            questionnaire.Id,
            AnonymousCapabilityHash.FromHash(UniqueHash()),
            now.AddHours(24),
            now);
        session.RecordAnswer(question, "{\"test\":true}", 1, now.AddMinutes(1));
        session.ReportSymptom(
            SymptomText.Create("User's original test phrase"),
            1,
            now.AddMinutes(1));
        var episode = PreTriageEpisode.CreateFrom(
            session,
            ruleSet.Id,
            now.AddMinutes(2),
            now.AddHours(24));
        var assessment = ClinicalAssessment.Create(
            episode,
            UrgencyCode.Create("test-only-urgency"),
            now.AddMinutes(2),
            [new ClinicalFindingInput("test-finding", "test-source-rule")]);
        return new CompletedGraph(
            questionnaire,
            ruleSet,
            session,
            episode,
            assessment);
    }

    private static QuestionnaireDefinitionVersion CreateQuestionnaire(
        string? code = null,
        string version = "test-version",
        bool includeQuestion = false)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create(code ?? $"test-questionnaire-{suffix}"),
            DefinitionVersion.Create(version),
            DefinitionHash.FromHash(new string('d', 64)),
            UtcNow(),
            UtcNow(),
            questions: includeQuestion
                ? [new TriageQuestionInput(
                    QuestionCode.Create("test-question"),
                    "Test-only prompt",
                    1)]
                : null);
    }

    private static ClinicalRuleSetVersion CreateRuleSet()
    {
        return ClinicalRuleSetVersion.ImportApproved(
            RuleSetCode.Create($"test-rule-set-{Guid.NewGuid():N}"),
            DefinitionVersion.Create("test-version"),
            DefinitionHash.FromHash(new string('e', 64)),
            UtcNow(),
            UtcNow());
    }

    private static PatientProfile CreatePatient()
    {
        return PatientProfile.Create(
            BeeexyId.Create($"BXY-TRIAGE-{Guid.NewGuid():N}"),
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

    private static string UniqueHash()
    {
        return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    }

    private static DateTimeOffset UtcNow()
    {
        return new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed record CompletedGraph(
        QuestionnaireDefinitionVersion Questionnaire,
        ClinicalRuleSetVersion RuleSet,
        PreTriageSession Session,
        PreTriageEpisode Episode,
        ClinicalAssessment Assessment);
}
