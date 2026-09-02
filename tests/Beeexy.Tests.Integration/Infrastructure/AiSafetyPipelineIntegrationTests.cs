using System.Text.Json;
using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase103")]
public sealed class AiSafetyPipelineIntegrationTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task ApprovedOutput_PersistsSnapshotSafetyTraceAndNoClinicalState()
    {
        var analysis = await CreateAnalysisAsync();
        var before = await NonAiCountsAsync();
        var raw = Json("Possible considerations include migraine.");
        var provider = Provider.Response(raw);

        var outcome = await ExecuteAsync(analysis.Id, provider);

        await using var dbContext = CreateDbContext();
        var execution = await dbContext.AiExecutions.AsNoTracking()
            .SingleAsync(value => value.Id == outcome.ExecutionId);
        var snapshot = await dbContext.AiResultSnapshots.AsNoTracking()
            .SingleAsync(value => value.ExecutionId == outcome.ExecutionId);
        var validation = await dbContext.AiSafetyValidations.AsNoTracking()
            .SingleAsync(value => value.ExecutionId == outcome.ExecutionId);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(AiExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(AiSafetyCategory.Approved, validation.Category);
        Assert.True(validation.DisplayEligible);
        Assert.Equal(snapshot.Id, validation.ResultSnapshotId);
        using (var persistedContent = JsonDocument.Parse(snapshot.ContentJson))
        {
            Assert.Equal("v1", persistedContent.RootElement
                .GetProperty("schemaVersion").GetString());
            Assert.Equal("Possible considerations include migraine.",
                persistedContent.RootElement.GetProperty("answer").GetString());
        }
        Assert.Null(validation.RestrictedAuditOutput);
        Assert.Equal("ai-safety-policy-v1", validation.PolicyVersion);
        Assert.Equal("ai-general-disclaimer-v1", validation.ProductContentVersion);
        Assert.Equal(snapshot.CreatedAt, validation.CreatedAt);
        Assert.True(outcome.ProviderOutputDisplayEligible);
        Assert.Equal(before, await NonAiCountsAsync());
    }

    [Theory]
    [InlineData(
        "You have diabetes. restricted-diagnosis-output",
        AiSafetyCategory.Diagnosis,
        "ai-rejection-fallback-v1")]
    [InlineData(
        "Start taking the medication. restricted-prescription-output",
        AiSafetyCategory.Prescription,
        "ai-rejection-fallback-v1")]
    [InlineData(
        "Call 911 now. restricted-emergency-output",
        AiSafetyCategory.UnsafeMedicalAdvice,
        "ai-critical-fallback-v1")]
    [InlineData(
        "UNSUPPORTED_OUTPUT_MARKER",
        AiSafetyCategory.Unsupported,
        "ai-rejection-fallback-v1")]
    public async Task RejectedOutput_IsRestrictedAndNeverCreatesDisplayableSnapshot(
        string text,
        AiSafetyCategory expectedCategory,
        string expectedContentVersion)
    {
        var analysis = await CreateAnalysisAsync();
        var raw = expectedCategory == AiSafetyCategory.Unsupported
            ? "{\"schemaVersion\":\"v1\",\"status\":\"unsupported\",\"answer\":" +
              JsonSerializer.Serialize(text) + "}"
            : Json(text);
        var provider = Provider.Response(raw);

        var outcome = await ExecuteAsync(analysis.Id, provider);

        await using var dbContext = CreateDbContext();
        var execution = await dbContext.AiExecutions.AsNoTracking()
            .SingleAsync(value => value.Id == outcome.ExecutionId);
        var validation = await dbContext.AiSafetyValidations.AsNoTracking()
            .SingleAsync(value => value.ExecutionId == outcome.ExecutionId);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(AiExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(expectedCategory, validation.Category);
        Assert.False(validation.DisplayEligible);
        Assert.Null(validation.ResultSnapshotId);
        Assert.Equal(raw, validation.RestrictedAuditOutput);
        Assert.Equal(expectedContentVersion, validation.ProductContentVersion);
        Assert.False(outcome.ProviderOutputDisplayEligible);
        Assert.DoesNotContain("restricted-", outcome.ResponseContent!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await dbContext.AiResultSnapshots.CountAsync(value =>
            value.ExecutionId == outcome.ExecutionId));

        var executionProperties = dbContext.Model.FindEntityType(typeof(AiExecution))!
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain(executionProperties, name =>
            name.Contains("Payload", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Content", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Audit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MalformedTechnicalResult_PreservesPhase102BoundaryWithoutSafetyRecord()
    {
        var analysis = await CreateAnalysisAsync();
        var provider = Provider.Response("not-json");

        var outcome = await ExecuteAsync(analysis.Id, provider);

        await using var dbContext = CreateDbContext();
        var execution = await dbContext.AiExecutions.AsNoTracking()
            .SingleAsync(value => value.Id == outcome.ExecutionId);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(AiExecutionOutcomeKind.MalformedResult, outcome.TechnicalOutcome);
        Assert.Equal(AiExecutionStatus.Rejected, execution.Status);
        Assert.Null(outcome.SafetyCategory);
        Assert.Equal(0, await dbContext.AiSafetyValidations.CountAsync(value =>
            value.ExecutionId == outcome.ExecutionId));
        Assert.Equal(0, await dbContext.AiResultSnapshots.CountAsync(value =>
            value.ExecutionId == outcome.ExecutionId));
    }

    private async Task<AiSafeAnalysisOutcome> ExecuteAsync(
        EntityId analysisId,
        Provider provider)
    {
        await using var dbContext = CreateDbContext();
        var clock = new AdvancingClock(Utc(10));
        var technical = new ExecuteAiAnalysis(
            clock,
            new ExecutionRepository(dbContext),
            new AiPromptResolver([new PromptContract()]),
            provider,
            new AiStructuredResultValidator([new Schema()]),
            new NullExecutionTelemetry());
        var subject = new ExecuteSafeAiAnalysis(
            technical,
            new BeeexyAiSafetyValidator(AiSafetyProductContent.Current),
            new SafetyPersistence(dbContext),
            new NullSafetyTelemetry(),
            AiSafetyProductContent.Current,
            clock);
        return await subject.ExecuteAsync(new ExecuteSafeAiAnalysisCommand(
            new ExecuteAiAnalysisCommand(
                analysisId,
                AiWorkloadIdentifiers.Conversation,
                new AiPromptIdentity("generic-analysis", "v1"),
                "private prepared input",
                new AiStructuredResultIdentity("generic-result", "v1"),
                $"trace-{Guid.NewGuid():N}")));
    }

    private async Task<AiAnalysisRequest> CreateAnalysisAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var account = Account.Create(
            NormalizedEmail.Create($"phase103-{suffix}@example.com"),
            Utc(9));
        var analysis = AiAnalysisRequest.Create(
            account.Id,
            AiAnalysisPurpose.Conversation,
            "generic-input-v1",
            "{\"privateInput\":\"content-bearing artifact\"}",
            Utc(9));
        dbContext.AddRange(account, analysis);
        await dbContext.SaveChangesAsync();
        return analysis;
    }

    private async Task<(int Episodes, int History, int FhirExports)> NonAiCountsAsync()
    {
        await using var dbContext = CreateDbContext();
        return (
            await dbContext.PreTriageEpisodes.CountAsync(),
            await dbContext.ClinicalHistoryEvents.CountAsync(),
            await dbContext.FhirExports.CountAsync());
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private sealed class ExecutionRepository(BeeexyDbContext dbContext)
        : IAiExecutionRepository
    {
        public void Add(AiExecution execution) => dbContext.AiExecutions.Add(execution);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed class SafetyPersistence(BeeexyDbContext dbContext) : IAiSafetyPersistence
    {
        public void AddApproved(AiResultSnapshot snapshot, AiSafetyValidation validation)
        {
            dbContext.AddRange(snapshot, validation);
        }

        public void AddRejected(AiSafetyValidation validation) =>
            dbContext.AiSafetyValidations.Add(validation);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed class Provider(
        Func<AiProviderRequest, Task<AiProviderResponse>> execute) : IAiProvider
    {
        public int CallCount { get; private set; }

        public string ProviderIdentifier => "one-provider";

        public string ModelIdentifier => "model-v1";

        public Task<AiProviderResponse> ExecuteAsync(
            AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return execute(request);
        }

        public static Provider Response(string content) => new(_ =>
            Task.FromResult(new AiProviderResponse(content)));
    }

    private sealed class PromptContract : IAiPromptContract
    {
        public AiPromptIdentity Identity { get; } = new("generic-analysis", "v1");

        public AiResolvedPrompt Build(string preparedInput) => new(
            Identity,
            "system",
            preparedInput);
    }

    private sealed class Schema : IAiStructuredResultSchema
    {
        public AiStructuredResultIdentity Identity { get; } = new("generic-result", "v1");

        public AiStructuralValidationResult Validate(JsonElement result) =>
            result.TryGetProperty("schemaVersion", out var version) &&
            version.GetString() == "v1" &&
            result.TryGetProperty("answer", out var answer) &&
            answer.ValueKind == JsonValueKind.String
                ? AiStructuralValidationResult.Valid
                : AiStructuralValidationResult.Invalid(
                    AiStructuralValidationIssue.InvalidStructure);
    }

    private sealed class NullExecutionTelemetry : IAiExecutionTelemetry
    {
        public void Started(AiExecution execution)
        {
        }

        public void Completed(AiExecution execution)
        {
        }
    }

    private sealed class NullSafetyTelemetry : IAiSafetyTelemetry
    {
        public void DecisionPersisted(AiSafetyValidation validation)
        {
        }
    }

    private sealed class AdvancingClock(DateTimeOffset now) : IClock
    {
        private DateTimeOffset current = now;

        public DateTimeOffset UtcNow
        {
            get
            {
                var value = current;
                current = current.AddSeconds(1);
                return value;
            }
        }
    }

    private static string Json(string text) => JsonSerializer.Serialize(new
    {
        schemaVersion = "v1",
        answer = text
    });

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 9, 1, hour, 0, 0, TimeSpan.Zero);
}
