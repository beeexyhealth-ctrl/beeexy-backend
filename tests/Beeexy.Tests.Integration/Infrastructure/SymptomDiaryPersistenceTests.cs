using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json.Nodes;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class SymptomDiaryPersistenceTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    [Trait("Category", "Phase91")]
    public async Task Migration_CreatesExactlySixCareTablesAndRequiredIndexes()
    {
        await EnsureMigratedAsync();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var tablesCommand = connection.CreateCommand();
        tablesCommand.CommandText =
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'care' AND table_type = 'BASE TABLE' ORDER BY table_name;";
        var tables = new List<string>();
        await using (var reader = await tablesCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Equal(
            [
                "symptom_check_in_answers",
                "symptom_check_ins",
                "symptom_diary_package_versions",
                "symptom_diary_question_options",
                "symptom_diary_questions",
                "symptom_warning_signs"
            ],
            tables);

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText =
            "SELECT count(*) FROM pg_indexes WHERE schemaname = 'care' AND indexname IN (" +
            "'ix_symptom_diary_packages_pathway_activation', " +
            "'ix_symptom_check_ins_episode_created_id', " +
            "'ix_symptom_check_in_answers_check_in_package');";
        Assert.Equal(3L, (long)(await indexCommand.ExecuteScalarAsync())!);
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public async Task PackageAndCheckInGraphs_PersistAndRoundTripExactOrderedContent()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(includeCheckIn: true);
        await SaveGraphAsync(graph);

        await using var verify = CreateDbContext();
        var package = await verify.SymptomDiaryPackageVersions
            .AsNoTracking()
            .Include(value => value.Questions)
            .ThenInclude(value => value.Options)
            .Include(value => value.WarningSigns)
            .SingleAsync(value => value.Id == graph.Package.Id);
        var checkIn = await verify.SymptomCheckIns
            .AsNoTracking()
            .Include(value => value.Answers)
            .SingleAsync(value => value.Id == graph.CheckIn!.Id);

        Assert.Equal(ClinicalContentSource.MedicalTeamProvided, package.ContentSource);
        Assert.Equal("test-question-set", package.QuestionSetCode.Value);
        Assert.Equal("test-information", package.SymptomInformationCode.Value);
        Assert.Equal([1, 2], package.Questions.OrderBy(value => value.SourceOrder).Select(value => value.SourceOrder));
        Assert.Equal(
            [1, 2],
            package.Questions.Single(value => value.SourceOrder == 1).Options
                .OrderBy(value => value.SourceOrder).Select(value => value.SourceOrder));
        Assert.Equal([1, 2], package.WarningSigns.OrderBy(value => value.SourceOrder).Select(value => value.SourceOrder));
        Assert.Equal(graph.Episode.Id, checkIn.EpisodeId);
        Assert.Equal(graph.Package.Id, checkIn.PackageVersionId);
        Assert.Equal(graph.Account.Id, checkIn.SubmittingAccountId);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("{\"values\":[\"one\",2,true]}"),
            JsonNode.Parse(Assert.Single(checkIn.Answers).SubmittedValueJson)));
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public async Task PostgreSql_EnforcesPackageAndIdempotencyUniqueness()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph();
        await SaveGraphAsync(graph);

        await using (var duplicatePackageContext = CreateDbContext())
        {
            duplicatePackageContext.SymptomDiaryPackageVersions.Add(CreatePackage(
                graph.Suffix,
                packageId: EntityId.New(),
                contentHash: new string('f', 64)));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                duplicatePackageContext.SaveChangesAsync());
            AssertConstraint(exception, "ux_symptom_diary_packages_code_version");
        }

        var key = EntityId.New();
        var first = CreateCheckIn(graph, key);
        var second = CreateCheckIn(graph, key);
        await using var duplicateKeyContext = CreateDbContext();
        duplicateKeyContext.AddRange(first, second);
        var duplicateKeyException = await Assert.ThrowsAsync<DbUpdateException>(() =>
            duplicateKeyContext.SaveChangesAsync());
        AssertConstraint(duplicateKeyException, "ux_symptom_check_ins_episode_idempotency");
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public async Task PostgreSql_EnforcesAccountAndExactPackageQuestionReferences()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(includeCheckIn: true);
        var otherPackage = CreatePackage($"other-{graph.Suffix}");
        await SaveGraphAsync(graph, otherPackage);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using (var missingRequired = connection.CreateCommand())
        {
            missingRequired.CommandText = "INSERT INTO care.symptom_check_ins (id) VALUES (@id);";
            missingRequired.Parameters.AddWithValue("id", Guid.NewGuid());
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                missingRequired.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.NotNullViolation, exception.SqlState);
        }

        await using (var invalidAccount = connection.CreateCommand())
        {
            invalidAccount.CommandText =
                "INSERT INTO care.symptom_check_ins " +
                "(id, episode_id, package_version_id, submitting_account_id, created_at, idempotency_key, canonical_request_hash) " +
                "VALUES (@id, @episode, @package, @account, @created, @key, @hash);";
            invalidAccount.Parameters.AddWithValue("id", Guid.NewGuid());
            invalidAccount.Parameters.AddWithValue("episode", graph.Episode.Id.Value);
            invalidAccount.Parameters.AddWithValue("package", graph.Package.Id.Value);
            invalidAccount.Parameters.AddWithValue("account", Guid.NewGuid());
            invalidAccount.Parameters.AddWithValue("created", UtcNow().AddHours(4));
            invalidAccount.Parameters.AddWithValue("key", Guid.NewGuid());
            invalidAccount.Parameters.AddWithValue("hash", new string('b', 64));
            var exception = await Assert.ThrowsAsync<PostgresException>(() => invalidAccount.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
            Assert.Equal("fk_symptom_check_ins_submitting_account", exception.ConstraintName);
        }

        await using var wrongQuestion = connection.CreateCommand();
        wrongQuestion.CommandText =
            "INSERT INTO care.symptom_check_in_answers " +
            "(id, check_in_id, package_version_id, question_id, submitted_value, source_order, recorded_at) " +
            "VALUES (@id, @checkin, @package, @question, 'true'::jsonb, 2, @recorded);";
        wrongQuestion.Parameters.AddWithValue("id", Guid.NewGuid());
        wrongQuestion.Parameters.AddWithValue("checkin", graph.CheckIn!.Id.Value);
        wrongQuestion.Parameters.AddWithValue("package", graph.Package.Id.Value);
        wrongQuestion.Parameters.AddWithValue("question", otherPackage.Questions.First().Id.Value);
        wrongQuestion.Parameters.AddWithValue("recorded", UtcNow().AddHours(4));
        var wrongQuestionException = await Assert.ThrowsAsync<PostgresException>(() =>
            wrongQuestion.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, wrongQuestionException.SqlState);
        Assert.Equal("fk_symptom_check_in_answers_question_package", wrongQuestionException.ConstraintName);
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public async Task PostgreSql_RejectsDuplicateAnswerAndDirectHistoricalMutation()
    {
        await EnsureMigratedAsync();
        var graph = CreateGraph(includeCheckIn: true);
        await SaveGraphAsync(graph);
        var answer = Assert.Single(graph.CheckIn!.Answers);

        await using (var guardedContext = CreateDbContext())
        {
            var persisted = await guardedContext.SymptomCheckIns
                .SingleAsync(value => value.Id == graph.CheckIn.Id);
            guardedContext.Entry(persisted).Property(nameof(SymptomCheckIn.CreatedAt))
                .CurrentValue = persisted.CreatedAt.AddSeconds(1);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                guardedContext.SaveChangesAsync());
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using (var duplicate = connection.CreateCommand())
        {
            duplicate.CommandText =
                "INSERT INTO care.symptom_check_in_answers " +
                "(id, check_in_id, package_version_id, question_id, submitted_value, source_order, recorded_at) " +
                "VALUES (@id, @checkin, @package, @question, 'false'::jsonb, @source_order, @recorded);";
            duplicate.Parameters.AddWithValue("id", Guid.NewGuid());
            duplicate.Parameters.AddWithValue("checkin", graph.CheckIn.Id.Value);
            duplicate.Parameters.AddWithValue("package", graph.Package.Id.Value);
            duplicate.Parameters.AddWithValue("question", answer.QuestionId.Value);
            duplicate.Parameters.AddWithValue("source_order", answer.SourceOrder);
            duplicate.Parameters.AddWithValue("recorded", answer.RecordedAt);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
            Assert.Equal("ux_symptom_check_in_answers_check_in_question", exception.ConstraintName);
        }

        await using (var update = connection.CreateCommand())
        {
            update.CommandText =
                "UPDATE care.symptom_check_ins SET created_at = created_at + interval '1 second' WHERE id = @id;";
            update.Parameters.AddWithValue("id", graph.CheckIn.Id.Value);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, exception.SqlState);
        }

        await using var deleteEpisode = connection.CreateCommand();
        deleteEpisode.CommandText = "DELETE FROM triage.pre_triage_episodes WHERE id = @id;";
        deleteEpisode.Parameters.AddWithValue("id", graph.Episode.Id.Value);
        var deleteException = await Assert.ThrowsAsync<PostgresException>(() =>
            deleteEpisode.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, deleteException.SqlState);
        Assert.Equal("fk_symptom_check_ins_episode", deleteException.ConstraintName);
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public async Task ExistingClinicalContentSourcesAndMedicalTeamSource_AllRoundTrip()
    {
        await EnsureMigratedAsync();
        var values = new[]
        {
            ClinicalContentStatus.LegacyApproved,
            ClinicalContentStatus.ProvisionalReferencePlatformDerived,
            ClinicalContentStatus.NonClinicalDemo,
            ClinicalContentStatus.MedicalTeamApproved
        };
        var versions = values.Select((status, index) => QuestionnaireDefinitionVersion.Import(
            ClinicalPathwayCode.Create($"TEST_SOURCE_{index}"),
            QuestionnaireCode.Create($"test-source-{Guid.NewGuid():N}"),
            DefinitionVersion.Create("v1"),
            DefinitionHash.FromHash(new string((char)('a' + index), 64)),
            status,
            UtcNow(),
            status.ApprovalStatus == ClinicalApprovalStatus.Approved ? UtcNow() : null)).ToArray();

        await using var dbContext = CreateDbContext();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        dbContext.QuestionnaireVersions.AddRange(versions);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.QuestionnaireVersions.AsNoTracking()
            .Where(value => versions.Select(item => item.Id).Contains(value.Id))
            .ToListAsync();
        Assert.Equal(
            values.Select(value => value.Source).OrderBy(value => value),
            persisted.Select(value => value.ContentSource).OrderBy(value => value));
        await transaction.RollbackAsync();
    }

    private async Task SaveGraphAsync(Graph graph, SymptomDiaryPackageVersion? extraPackage = null)
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(
            graph.Account,
            graph.Patient,
            graph.Questionnaire,
            graph.RuleSet,
            graph.Session,
            graph.Episode,
            graph.Package);
        if (graph.CheckIn is not null)
        {
            dbContext.SymptomCheckIns.Add(graph.CheckIn);
        }

        if (extraPackage is not null)
        {
            dbContext.SymptomDiaryPackageVersions.Add(extraPackage);
        }

        await dbContext.SaveChangesAsync();
    }

    private Graph CreateGraph(bool includeCheckIn = false)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var account = Account.Create(
            NormalizedEmail.Create($"diary-{suffix}@example.com"),
            UtcNow());
        var patient = PatientProfile.Create(
            BeeexyId.Create($"BXY-DIARY-{suffix}"),
            UtcNow(),
            account.Id);
        var questionnaire = QuestionnaireDefinitionVersion.Import(
            TestPathway,
            QuestionnaireCode.Create($"test-diary-questionnaire-{suffix}"),
            DefinitionVersion.Create("v1"),
            DefinitionHash.FromHash(new string('c', 64)),
            ClinicalContentStatus.NonClinicalDemo,
            UtcNow());
        var ruleSet = ClinicalRuleSetVersion.Import(
            TestPathway,
            RuleSetCode.Create($"test-diary-rules-{suffix}"),
            DefinitionVersion.Create("v1"),
            DefinitionHash.FromHash(new string('d', 64)),
            ClinicalContentStatus.NonClinicalDemo,
            "{}",
            UtcNow());
        var session = PreTriageSession.CreateForPatient(
            patient.Id,
            questionnaire.Id,
            UtcNow().AddHours(8),
            UtcNow());
        var episode = PreTriageEpisode.CreateFrom(session, ruleSet.Id, UtcNow().AddHours(1));
        var package = CreatePackage(suffix);
        var graph = new Graph(
            suffix,
            account,
            patient,
            questionnaire,
            ruleSet,
            session,
            episode,
            package,
            null);
        return includeCheckIn ? graph with { CheckIn = CreateCheckIn(graph, EntityId.New(), true) } : graph;
    }

    private static SymptomDiaryPackageVersion CreatePackage(
        string suffix,
        EntityId? packageId = null,
        string? contentHash = null)
        => SymptomDiaryPackageVersion.Import(
            SymptomDiaryCode.Create($"test-diary-{suffix}"),
            DefinitionVersion.Create("v1"),
            SymptomDiaryCode.Create("test-question-set"),
            DefinitionVersion.Create("q1"),
            SymptomDiaryCode.Create("test-information"),
            DefinitionVersion.Create("i1"),
            TestPathway,
            SymptomDiarySha256.FromHash(contentHash ?? new string('a', 64)),
            ClinicalContentStatus.MedicalTeamApproved,
            UtcNow(),
            UtcNow(),
            UtcNow().AddHours(1),
            $"test/source/{suffix}",
            "Synthetic informational heading",
            "Synthetic informational body",
            [
                new(
                    SymptomDiaryCode.Create("second-question"),
                    "Synthetic second prompt",
                    2,
                    "{\"type\":\"boolean\"}",
                    false),
                new(
                    SymptomDiaryCode.Create("first-question"),
                    "Synthetic first prompt",
                    1,
                    "{\"type\":\"object\"}",
                    true,
                    [
                        new(SymptomDiaryCode.Create("second-option"), "two", "Synthetic two", 2),
                        new(SymptomDiaryCode.Create("first-option"), "one", "Synthetic one", 1)
                    ])
            ],
            [
                new(SymptomDiaryCode.Create("second-information"), "Synthetic information B", 2),
                new(SymptomDiaryCode.Create("first-information"), "Synthetic information A", 1)
            ],
            packageId);

    private static SymptomCheckIn CreateCheckIn(Graph graph, EntityId key, bool includeAnswer = false)
        => SymptomCheckIn.Create(
            graph.Episode,
            graph.Questionnaire,
            graph.Package,
            graph.Account.Id,
            key,
            SymptomDiarySha256.FromHash(new string('b', 64)),
            UtcNow().AddHours(2),
            includeAnswer
                ? [new(graph.Package.Questions.First(), "{\"values\":[\"one\",2,true]}")]
                : null);

    private static void AssertConstraint(DbUpdateException exception, string expected)
    {
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(expected, postgresException.ConstraintName);
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private static ClinicalPathwayCode TestPathway { get; } =
        ClinicalPathwayCode.Create("TEST_DIARY_PATHWAY");

    private static DateTimeOffset UtcNow()
        => new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private sealed record Graph(
        string Suffix,
        Account Account,
        PatientProfile Patient,
        QuestionnaireDefinitionVersion Questionnaire,
        ClinicalRuleSetVersion RuleSet,
        PreTriageSession Session,
        PreTriageEpisode Episode,
        SymptomDiaryPackageVersion Package,
        SymptomCheckIn? CheckIn);
}
