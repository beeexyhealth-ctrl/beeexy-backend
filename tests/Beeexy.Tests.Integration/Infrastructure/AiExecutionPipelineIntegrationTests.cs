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
[Trait("Category", "Phase102")]
public sealed class AiExecutionPipelineIntegrationTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task SuccessfulExecution_PersistsTraceWithoutClinicalOrFhirMutation()
    {
        var analysis = await CreateAnalysisAsync();
        var before = await NonAiCountsAsync();
        var provider = Provider.Response(
            "{\"schemaVersion\":\"v1\",\"answer\":\"internal only\"}");

        var result = await ExecuteAsync(analysis.Id, provider);

        await using var dbContext = CreateDbContext();
        var execution = await dbContext.AiExecutions.AsNoTracking()
            .SingleAsync(value => value.Id == result.ExecutionId);
        Assert.Equal(AiExecutionOutcomeKind.StructurallyValid, result.Kind);
        Assert.Equal(AiExecutionStatus.Succeeded, execution.Status);
        Assert.Equal("integration-provider", execution.ProviderIdentifier);
        Assert.Equal("model-v1", execution.ModelIdentifier);
        Assert.Equal("generic-analysis@v1", execution.PromptVersion);
        Assert.Equal(1_000, execution.LatencyMilliseconds);
        Assert.Null(execution.SanitizedFailureCategory);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(before, await NonAiCountsAsync());
    }

    [Fact]
    public async Task MalformedStructuredResult_IsRejectedAndDoesNotRemainRunning()
    {
        var analysis = await CreateAnalysisAsync();
        var provider = Provider.Response("{\"schemaVersion\":\"v1\",\"answer\":1}");

        var result = await ExecuteAsync(analysis.Id, provider);

        await using var dbContext = CreateDbContext();
        var execution = await dbContext.AiExecutions.AsNoTracking()
            .SingleAsync(value => value.Id == result.ExecutionId);
        Assert.Equal(AiExecutionOutcomeKind.MalformedResult, result.Kind);
        Assert.Equal(AiExecutionStatus.Rejected, execution.Status);
        Assert.Null(execution.SanitizedFailureCategory);
        Assert.Equal(1, provider.CallCount);
        Assert.False(await dbContext.AiExecutions.AnyAsync(value =>
            value.Id == result.ExecutionId && value.Status == AiExecutionStatus.Running));
    }

    [Theory]
    [InlineData(
        AiProviderFailureCategory.Timeout,
        AiExecutionOutcomeKind.Timeout,
        "timeout")]
    [InlineData(
        AiProviderFailureCategory.Transient,
        AiExecutionOutcomeKind.TransientFailure,
        "provider_transient")]
    [InlineData(
        AiProviderFailureCategory.Permanent,
        AiExecutionOutcomeKind.PermanentFailure,
        "provider_permanent")]
    [InlineData(
        AiProviderFailureCategory.ConfigurationUnavailable,
        AiExecutionOutcomeKind.ConfigurationUnavailable,
        "configuration_unavailable")]
    public async Task ProviderFailure_PersistsSanitizedTerminalMetadata(
        AiProviderFailureCategory category,
        AiExecutionOutcomeKind expectedOutcome,
        string expectedFailure)
    {
        var analysis = await CreateAnalysisAsync();
        var provider = Provider.Failure(new AiProviderException(category));

        var result = await ExecuteAsync(analysis.Id, provider);

        await using var dbContext = CreateDbContext();
        var execution = await dbContext.AiExecutions.AsNoTracking()
            .SingleAsync(value => value.Id == result.ExecutionId);
        Assert.Equal(expectedOutcome, result.Kind);
        Assert.Equal(AiExecutionStatus.Failed, execution.Status);
        Assert.Equal(expectedFailure, execution.SanitizedFailureCategory);
        Assert.NotNull(execution.CompletedAt);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task CallerCancellationAfterStart_PersistsTerminalFailure()
    {
        var analysis = await CreateAnalysisAsync();
        using var cancellation = new CancellationTokenSource();
        var provider = new Provider((_, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        var result = await ExecuteAsync(analysis.Id, provider, cancellation.Token);

        await using var dbContext = CreateDbContext();
        var execution = await dbContext.AiExecutions.AsNoTracking()
            .SingleAsync(value => value.Id == result.ExecutionId);
        Assert.Equal(AiExecutionOutcomeKind.CallerCancelled, result.Kind);
        Assert.Equal(AiExecutionStatus.Failed, execution.Status);
        Assert.Equal("caller_cancellation", execution.SanitizedFailureCategory);
        Assert.Equal(1, provider.CallCount);
    }

    private async Task<AiExecutionOutcome> ExecuteAsync(
        EntityId analysisId,
        Provider provider,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = CreateDbContext();
        var subject = new ExecuteAiAnalysis(
            new AdvancingClock(Utc(10)),
            new Repository(dbContext),
            new AiPromptResolver([new PromptContract()]),
            provider,
            new AiStructuredResultValidator([new Schema()]),
            new NullTelemetry());
        return await subject.ExecuteAsync(
            new ExecuteAiAnalysisCommand(
                analysisId,
                "generic-workload",
                new AiPromptIdentity("generic-analysis", "v1"),
                "private normalized input",
                new AiStructuredResultIdentity("generic-result", "v1"),
                $"trace-{Guid.NewGuid():N}"),
            cancellationToken);
    }

    private async Task<AiAnalysisRequest> CreateAnalysisAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var account = Account.Create(
            NormalizedEmail.Create($"phase102-{suffix}@example.com"),
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

    private sealed class Repository(BeeexyDbContext dbContext) : IAiExecutionRepository
    {
        public void Add(AiExecution execution) => dbContext.AiExecutions.Add(execution);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed class Provider(
        Func<AiProviderRequest, CancellationToken, Task<AiProviderResponse>> execute)
        : IAiProvider
    {
        public int CallCount { get; private set; }

        public string ProviderIdentifier => "integration-provider";

        public string ModelIdentifier => "model-v1";

        public Task<AiProviderResponse> ExecuteAsync(
            AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return execute(request, cancellationToken);
        }

        public static Provider Response(string content) => new((_, _) =>
            Task.FromResult(new AiProviderResponse(content)));

        public static Provider Failure(Exception exception) => new((_, _) =>
            Task.FromException<AiProviderResponse>(exception));
    }

    private sealed class PromptContract : IAiPromptContract
    {
        public AiPromptIdentity Identity { get; } = new("generic-analysis", "v1");

        public AiResolvedPrompt Build(string preparedInput) => new(
            Identity,
            "system instructions",
            preparedInput);
    }

    private sealed class Schema : IAiStructuredResultSchema
    {
        public AiStructuredResultIdentity Identity { get; } = new("generic-result", "v1");

        public AiStructuralValidationResult Validate(JsonElement result)
        {
            if (!result.TryGetProperty("schemaVersion", out var version) ||
                version.ValueKind != JsonValueKind.String ||
                version.GetString() != Identity.Version)
            {
                return AiStructuralValidationResult.Invalid(
                    AiStructuralValidationIssue.SchemaVersionMismatch);
            }

            return result.TryGetProperty("answer", out var answer) &&
                answer.ValueKind == JsonValueKind.String
                    ? AiStructuralValidationResult.Valid
                    : AiStructuralValidationResult.Invalid(
                        AiStructuralValidationIssue.InvalidFieldType);
        }
    }

    private sealed class NullTelemetry : IAiExecutionTelemetry
    {
        public void Started(AiExecution execution)
        {
        }

        public void Completed(AiExecution execution)
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

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 9, 1, hour, 0, 0, TimeSpan.Zero);
}
