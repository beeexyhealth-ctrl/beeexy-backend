using Beeexy.Domain.Ai;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase101")]
[Trait("Category", "Phase108")]
public sealed class AiPersistenceFoundationTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task CompleteProviderNeutralGraph_RoundTripsWithTwoImmutableSnapshots()
    {
        await EnsureMigratedAsync();
        var now = Utc(10);
        var (account, patient) = CreateAccountAndPatient(now);
        await SaveAsync(account, patient);

        var conversation = AiConversation.Create(account.Id, now, patient.Id);
        var userMessage = AiMessage.Create(
            conversation.Id,
            AiMessageRole.User,
            "Dedicated message content",
            1,
            now.AddMinutes(1));
        var assistantMessage = AiMessage.Create(
            conversation.Id,
            AiMessageRole.Assistant,
            "Dedicated assistant content",
            2,
            now.AddMinutes(2));
        var analysis = AiAnalysisRequest.Create(
            account.Id,
            AiAnalysisPurpose.SecondOpinion,
            "analysis-input-v1",
            "{\"text\":\"normalized original input\",\"sourceReferences\":[]}",
            now.AddMinutes(3),
            patient.Id,
            conversation.Id);
        var firstExecution = SuccessfulExecution(analysis.Id, now.AddMinutes(4));
        var secondExecution = SuccessfulExecution(analysis.Id, now.AddMinutes(7));
        var firstSnapshot = AiResultSnapshot.Create(
            analysis.Id,
            firstExecution.Id,
            1,
            "result-v1",
            "{\"summary\":\"first immutable result\"}",
            now.AddMinutes(6));
        var secondSnapshot = AiResultSnapshot.Create(
            analysis.Id,
            secondExecution.Id,
            2,
            "result-v1",
            "{\"summary\":\"regenerated immutable result\"}",
            now.AddMinutes(9));
        var firstSafety = AiSafetyValidation.CreateApproved(
            firstExecution.Id,
            firstSnapshot.Id,
            "safety-v1",
            now.AddMinutes(6),
            "disclaimer-v1");
        var secondSafety = AiSafetyValidation.CreateApproved(
            secondExecution.Id,
            secondSnapshot.Id,
            "safety-v1",
            now.AddMinutes(9),
            "disclaimer-v1");
        var document = AiUploadedDocument.Create(
            account.Id,
            $"private/{Guid.NewGuid():N}",
            "application/pdf",
            1024,
            now,
            now.AddHours(24),
            patient.Id);
        Assert.True(document.AssociateWithAnalysis(analysis.Id));

        await SaveAsync(
            conversation,
            userMessage,
            assistantMessage,
            analysis,
            firstExecution,
            secondExecution,
            firstSnapshot,
            secondSnapshot,
            firstSafety,
            secondSafety,
            document);

        await using var dbContext = CreateDbContext();
        var messages = await dbContext.AiMessages.AsNoTracking()
            .Where(value => value.ConversationId == conversation.Id)
            .OrderBy(value => value.Sequence)
            .ToArrayAsync();
        var snapshots = await dbContext.AiResultSnapshots.AsNoTracking()
            .Where(value => value.AnalysisRequestId == analysis.Id)
            .OrderBy(value => value.Sequence)
            .ToArrayAsync();
        var savedExecution = await dbContext.AiExecutions.AsNoTracking()
            .SingleAsync(value => value.Id == firstExecution.Id);
        var savedDocument = await dbContext.AiUploadedDocuments.AsNoTracking()
            .SingleAsync(value => value.Id == document.Id);

        Assert.Equal([1, 2], messages.Select(value => value.Sequence));
        Assert.Equal([1, 2], snapshots.Select(value => value.Sequence));
        Assert.Contains("first immutable result", snapshots[0].ContentJson);
        Assert.Contains("regenerated immutable result", snapshots[1].ContentJson);
        Assert.Equal("provider-neutral-test", savedExecution.ProviderIdentifier);
        Assert.Equal("model-v1", savedExecution.ModelIdentifier);
        Assert.Equal("second-opinion-prompt-v1", savedExecution.PromptVersion);
        Assert.Equal(analysis.Id, savedDocument.AnalysisRequestId);
        Assert.Equal(AiDocumentStatus.Active, savedDocument.Status);
        Assert.Equal(
            2,
            await dbContext.AiSafetyValidations.AsNoTracking()
                .CountAsync(value => value.DisplayEligible));
    }

    [Fact]
    public async Task OptionalPatientAssociationsAndLogicalDeletion_RoundTrip()
    {
        await EnsureMigratedAsync();
        var now = Utc(10);
        var (account, _) = CreateAccountAndPatient(now);
        await SaveAsync(account);
        var conversation = AiConversation.Create(account.Id, now);
        var analysis = AiAnalysisRequest.Create(
            account.Id,
            AiAnalysisPurpose.Conversation,
            "conversation-input-v1",
            "{}",
            now,
            conversationId: conversation.Id);
        var document = AiUploadedDocument.Create(
            account.Id,
            $"private/{Guid.NewGuid():N}",
            "text/plain",
            128,
            now,
            now.AddHours(24));

        await SaveAsync(conversation, analysis, document);

        await using (var dbContext = CreateDbContext())
        {
            var savedConversation = await dbContext.AiConversations
                .SingleAsync(value => value.Id == conversation.Id);
            Assert.Null(savedConversation.PatientProfileId);
            Assert.True(savedConversation.Delete(now.AddHours(1)));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext())
        {
            var savedConversation = await dbContext.AiConversations.AsNoTracking()
                .SingleAsync(value => value.Id == conversation.Id);
            Assert.Equal(now.AddHours(1), savedConversation.DeletedAt);
            Assert.Null((await dbContext.AiAnalysisRequests.AsNoTracking()
                .SingleAsync(value => value.Id == analysis.Id)).PatientProfileId);
            Assert.Null((await dbContext.AiUploadedDocuments.AsNoTracking()
                .SingleAsync(value => value.Id == document.Id)).PatientProfileId);
        }
    }

    [Fact]
    public async Task RejectedSafetyAuditAndDocumentLifecycle_RoundTripWithoutExecutionPayload()
    {
        await EnsureMigratedAsync();
        var now = Utc(10);
        var (account, _) = CreateAccountAndPatient(now);
        await SaveAsync(account);
        var analysis = AiAnalysisRequest.Create(
            account.Id,
            AiAnalysisPurpose.SecondOpinion,
            "analysis-input-v1",
            "{\"text\":\"dedicated input artifact\"}",
            now);
        var execution = AiExecution.CreatePending(analysis.Id, now.AddMinutes(1));
        execution.Start(
            "provider-neutral-test",
            "model-v1",
            "second-opinion-prompt-v1",
            now.AddMinutes(2));
        execution.MarkRejected(now.AddMinutes(3));
        var safety = AiSafetyValidation.CreateRejected(
            execution.Id,
            AiSafetyCategory.Diagnosis,
            "safety-v1",
            "restricted rejected output",
            now.AddMinutes(3),
            "fallback-v1");
        var document = AiUploadedDocument.Create(
            account.Id,
            $"private/{Guid.NewGuid():N}",
            "text/plain",
            64,
            now,
            now.AddHours(24));
        Assert.True(document.MarkDeleted(now.AddHours(1)));

        await SaveAsync(analysis, execution, safety, document);

        await using var dbContext = CreateDbContext();
        var savedSafety = await dbContext.AiSafetyValidations.AsNoTracking()
            .SingleAsync(value => value.Id == safety.Id);
        var savedDocument = await dbContext.AiUploadedDocuments.AsNoTracking()
            .SingleAsync(value => value.Id == document.Id);
        var executionColumns = dbContext.Model.FindEntityType(typeof(AiExecution))!
            .GetProperties()
            .Select(value => value.Name)
            .ToArray();

        Assert.Equal(AiSafetyCategory.Diagnosis, savedSafety.Category);
        Assert.False(savedSafety.DisplayEligible);
        Assert.Equal("restricted rejected output", savedSafety.RestrictedAuditOutput);
        Assert.Equal(AiDocumentStatus.Deleted, savedDocument.Status);
        Assert.DoesNotContain(executionColumns, value =>
            value.Contains("Payload", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Content", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("PromptText", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PostgreSqlConstraints_RejectDuplicateOrderAndMismatchedExecutionAnalysis()
    {
        await EnsureMigratedAsync();
        var now = Utc(10);
        var (account, _) = CreateAccountAndPatient(now);
        await SaveAsync(account);
        var conversation = AiConversation.Create(account.Id, now);
        await SaveAsync(conversation);
        await SaveAsync(AiMessage.Create(
            conversation.Id,
            AiMessageRole.User,
            "first",
            1,
            now));

        await AssertDatabaseViolationAsync(
            () => SaveAsync(AiMessage.Create(
                conversation.Id,
                AiMessageRole.Assistant,
                "duplicate sequence",
                1,
                now.AddMinutes(1))),
            PostgresErrorCodes.UniqueViolation,
            "ux_ai_messages_conversation_sequence");

        var firstAnalysis = AiAnalysisRequest.Create(
            account.Id,
            AiAnalysisPurpose.SecondOpinion,
            "input-v1",
            "{}",
            now);
        var secondAnalysis = AiAnalysisRequest.Create(
            account.Id,
            AiAnalysisPurpose.SecondOpinion,
            "input-v1",
            "{}",
            now);
        var execution = SuccessfulExecution(firstAnalysis.Id, now.AddMinutes(1));
        await SaveAsync(firstAnalysis, secondAnalysis, execution);

        await AssertDatabaseViolationAsync(
            () => SaveAsync(AiResultSnapshot.Create(
                secondAnalysis.Id,
                execution.Id,
                1,
                "result-v1",
                "{}",
                now.AddMinutes(3))),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_ai_result_snapshots_execution_analysis");
    }

    [Fact]
    public async Task RestrictiveForeignKeysAndContextGuards_PreserveAuditHistory()
    {
        await EnsureMigratedAsync();
        var now = Utc(10);
        var (account, patient) = CreateAccountAndPatient(now);
        var conversation = AiConversation.Create(account.Id, now, patient.Id);
        var analysis = AiAnalysisRequest.Create(
            account.Id,
            AiAnalysisPurpose.SecondOpinion,
            "input-v1",
            "{}",
            now,
            patient.Id,
            conversation.Id);
        var execution = SuccessfulExecution(analysis.Id, now.AddMinutes(1));
        var snapshot = AiResultSnapshot.Create(
            analysis.Id,
            execution.Id,
            1,
            "result-v1",
            "{\"summary\":\"immutable\"}",
            now.AddMinutes(3));
        var safety = AiSafetyValidation.CreateApproved(
            execution.Id,
            snapshot.Id,
            "safety-v1",
            now.AddMinutes(3));
        await SaveAsync(account, patient);
        await SaveAsync(conversation, analysis, execution, snapshot, safety);

        await using (var dbContext = CreateDbContext())
        {
            var trackedSnapshot = await dbContext.AiResultSnapshots
                .SingleAsync(value => value.Id == snapshot.Id);
            dbContext.Entry(trackedSnapshot).Property(value => value.ContentJson)
                .CurrentValue = "{\"summary\":\"replacement\"}";
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                dbContext.SaveChangesAsync());
        }

        await using (var dbContext = CreateDbContext())
        {
            var trackedConversation = await dbContext.AiConversations
                .SingleAsync(value => value.Id == conversation.Id);
            dbContext.AiConversations.Remove(trackedConversation);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                dbContext.SaveChangesAsync());
        }

        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM identity.accounts WHERE id = @id;";
            command.Parameters.AddWithValue("id", account.Id.Value);
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        }
    }

    [Fact]
    public async Task AiSchema_HasSevenUuidTablesRestrictiveForeignKeysChecksAndRequiredIndexes()
    {
        await EnsureMigratedAsync();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        var tables = await QueryStringsAsync(
            connection,
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'ai' AND table_type = 'BASE TABLE' ORDER BY table_name;");
        Assert.Equal(
            [
                "ai_analysis_requests",
                "ai_conversations",
                "ai_executions",
                "ai_messages",
                "ai_result_snapshots",
                "ai_safety_validations",
                "ai_uploaded_documents"
            ],
            tables);

        var uuidPrimaryKeys = await QueryStringsAsync(
            connection,
            "SELECT c.table_name FROM information_schema.columns c " +
            "JOIN information_schema.table_constraints tc " +
            "ON tc.table_schema = c.table_schema AND tc.table_name = c.table_name " +
            "JOIN information_schema.key_column_usage kcu " +
            "ON kcu.constraint_schema = tc.constraint_schema " +
            "AND kcu.constraint_name = tc.constraint_name AND kcu.column_name = c.column_name " +
            "WHERE c.table_schema = 'ai' AND c.column_name = 'id' " +
            "AND c.data_type = 'uuid' AND tc.constraint_type = 'PRIMARY KEY' " +
            "ORDER BY c.table_name;");
        Assert.Equal(tables, uuidPrimaryKeys);

        var nonRestrictiveForeignKeys = await QueryStringsAsync(
            connection,
            "SELECT conname FROM pg_constraint " +
            "WHERE connamespace = 'ai'::regnamespace AND contype = 'f' AND confdeltype <> 'r';");
        Assert.Empty(nonRestrictiveForeignKeys);

        var indexes = await QueryStringsAsync(
            connection,
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'ai' ORDER BY indexname;");
        var requiredIndexes = new[]
        {
            "ix_ai_analysis_requests_account_created_id",
            "ix_ai_analysis_requests_patient_created_id",
            "ix_ai_conversations_account_created_id",
            "ix_ai_conversations_patient_created_id",
            "ix_ai_executions_analysis_created_id",
            "ix_ai_executions_status_created_id",
            "ix_ai_result_snapshots_analysis_created_id",
            "ix_ai_uploaded_documents_account_created_id",
            "ix_ai_uploaded_documents_status_expiry_id",
            "ux_ai_messages_conversation_sequence",
            "ux_ai_result_snapshots_analysis_sequence",
            "ux_ai_result_snapshots_execution",
            "ux_ai_safety_validations_execution",
            "ux_ai_uploaded_documents_storage_key"
        };
        Assert.All(requiredIndexes, value => Assert.Contains(value, indexes));

        var checkConstraintCount = await ScalarLongAsync(
            connection,
            "SELECT count(*) FROM pg_constraint " +
            "WHERE connamespace = 'ai'::regnamespace AND contype = 'c';");
        Assert.True(checkConstraintCount >= 17);
    }

    private BeeexyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options;
        return new BeeexyDbContext(options);
    }

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private async Task SaveAsync(params object[] entities)
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(entities);
        await dbContext.SaveChangesAsync();
    }

    private static (Account Account, PatientProfile Patient) CreateAccountAndPatient(
        DateTimeOffset now)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var account = Account.Create(
            NormalizedEmail.Create($"ai-{suffix}@example.com"),
            now);
        var patient = PatientProfile.Create(
            BeeexyId.Create($"BXY-AI-{suffix}"),
            now,
            account.Id);
        return (account, patient);
    }

    private static AiExecution SuccessfulExecution(
        Beeexy.Domain.Common.EntityId analysisId,
        DateTimeOffset createdAt)
    {
        var execution = AiExecution.CreatePending(analysisId, createdAt);
        execution.Start(
            "provider-neutral-test",
            "model-v1",
            "second-opinion-prompt-v1",
            createdAt.AddMinutes(1));
        execution.MarkSucceeded(createdAt.AddMinutes(2));
        return execution;
    }

    private static async Task AssertDatabaseViolationAsync(
        Func<Task> action,
        string sqlState,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgresException.SqlState);
        Assert.Equal(constraintName, postgresException.ConstraintName);
    }

    private static async Task<string[]> QueryStringsAsync(
        NpgsqlConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(2026, 9, 1, hour, 0, 0, TimeSpan.Zero);
    }
}
