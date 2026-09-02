using System.Text.Json;
using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Tests.Unit.Ai;

[Trait("Category", "Phase102")]
[Trait("Category", "Phase108")]
public sealed class ExecuteAiAnalysisTests
{
    [Fact]
    public async Task Success_CallsOneProviderAndPersistsCompleteTechnicalTrace()
    {
        var harness = Harness.Success();

        var result = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(AiExecutionOutcomeKind.StructurallyValid, result.Kind);
        Assert.True(result.RequiresSafetyValidation);
        Assert.Contains("informational", result.StructurallyValidatedContent);
        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Equal(3, harness.Repository.SaveCount);
        var execution = Assert.IsType<AiExecution>(harness.Repository.Execution);
        Assert.Equal(AiExecutionStatus.Succeeded, execution.Status);
        Assert.Equal("test-provider", execution.ProviderIdentifier);
        Assert.Equal("model-v1", execution.ModelIdentifier);
        Assert.Equal("generic-analysis@v2", execution.PromptVersion);
        Assert.Equal(1_000, execution.LatencyMilliseconds);
        Assert.Null(execution.SanitizedFailureCategory);
        Assert.Equal(1, harness.Telemetry.StartedCount);
        Assert.Equal(1, harness.Telemetry.CompletedCount);
    }

    [Fact]
    public async Task RequestTranslation_IsProviderNeutralAndPreservesExactIdentities()
    {
        var harness = Harness.Success();

        await harness.Subject.ExecuteAsync(Command());

        var request = Assert.IsType<AiProviderRequest>(harness.Provider.LastRequest);
        Assert.Equal("generic-workload", request.WorkloadIdentifier);
        Assert.Equal(new AiPromptIdentity("generic-analysis", "v2"), request.Prompt);
        Assert.Equal("System contract v2", request.SystemInstructions);
        Assert.Equal("prepared:normalized private input", request.UserContent);
        Assert.Equal(new AiStructuredResultIdentity("generic-result", "v1"),
            request.ExpectedResult);
        Assert.Equal("trace-123", request.CorrelationIdentifier);
        Assert.DoesNotContain("Nvidia", request.GetType().AssemblyQualifiedName,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidStructuredOutput_IsRejectedWithoutSafetyApproval()
    {
        var harness = Harness.WithProviderResponse("not-json");

        var result = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(AiExecutionOutcomeKind.MalformedResult, result.Kind);
        Assert.False(result.RequiresSafetyValidation);
        Assert.Null(result.StructurallyValidatedContent);
        Assert.Equal(AiStructuralValidationIssue.InvalidJson, result.StructuralIssue);
        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Equal(AiExecutionStatus.Rejected, harness.Repository.Execution!.Status);
        Assert.Null(harness.Repository.Execution.SanitizedFailureCategory);
    }

    [Fact]
    public async Task ProviderMalformedEnvelope_IsRejectedWithoutRetry()
    {
        var harness = Harness.WithProviderFailure(
            new AiProviderException(AiProviderFailureCategory.MalformedResponse));

        var result = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(AiExecutionOutcomeKind.MalformedResult, result.Kind);
        Assert.Equal(AiStructuralValidationIssue.InvalidStructure, result.StructuralIssue);
        Assert.Equal(AiExecutionStatus.Rejected, harness.Repository.Execution!.Status);
        Assert.Equal(1, harness.Provider.CallCount);
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
    public async Task ProviderFailures_AreSanitizedAndTerminalWithoutRetry(
        AiProviderFailureCategory category,
        AiExecutionOutcomeKind expectedOutcome,
        string expectedFailure)
    {
        var harness = Harness.WithProviderFailure(new AiProviderException(category));

        var result = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(expectedOutcome, result.Kind);
        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Equal(AiExecutionStatus.Failed, harness.Repository.Execution!.Status);
        Assert.Equal(expectedFailure, harness.Repository.Execution.SanitizedFailureCategory);
        Assert.Null(result.StructurallyValidatedContent);
    }

    [Fact]
    public async Task CallerCancellation_IsHonoredAndTerminalStateIsPersisted()
    {
        using var cancellation = new CancellationTokenSource();
        var harness = Harness.WithProvider((_, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        var result = await harness.Subject.ExecuteAsync(Command(), cancellation.Token);

        Assert.Equal(AiExecutionOutcomeKind.CallerCancelled, result.Kind);
        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Equal(AiExecutionStatus.Failed, harness.Repository.Execution!.Status);
        Assert.Equal("caller_cancellation",
            harness.Repository.Execution.SanitizedFailureCategory);
        Assert.Equal(CancellationToken.None, harness.Repository.LastSaveToken);
    }

    [Fact]
    public async Task NonCallerCancellation_IsNormalizedAsTimeout()
    {
        var harness = Harness.WithProvider((_, _) =>
            throw new OperationCanceledException());

        var result = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(AiExecutionOutcomeKind.Timeout, result.Kind);
        Assert.Equal("timeout", harness.Repository.Execution!.SanitizedFailureCategory);
        Assert.Equal(1, harness.Provider.CallCount);
    }

    [Fact]
    public async Task UnexpectedProviderException_DoesNotEscapeOrRevealDetails()
    {
        var harness = Harness.WithProvider((_, _) =>
            throw new InvalidOperationException("raw provider body and secret"));

        var result = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(AiExecutionOutcomeKind.PermanentFailure, result.Kind);
        Assert.Equal("provider_permanent",
            harness.Repository.Execution!.SanitizedFailureCategory);
        Assert.DoesNotContain("raw provider", harness.Repository.Execution
            .SanitizedFailureCategory, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, harness.Provider.CallCount);
    }

    [Fact]
    public async Task NullProviderResponse_IsSafePermanentFailure()
    {
        var harness = Harness.WithProvider((_, _) =>
            Task.FromResult<AiProviderResponse>(null!));

        var result = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(AiExecutionOutcomeKind.PermanentFailure, result.Kind);
        Assert.Equal(AiExecutionStatus.Failed, harness.Repository.Execution!.Status);
        Assert.Equal(1, harness.Provider.CallCount);
    }

    [Fact]
    public async Task MissingPromptVersion_FailsBeforeExecutionOrProviderCall()
    {
        var harness = Harness.Success();
        var command = new ExecuteAiAnalysisCommand(
            EntityId.New(),
            "generic-workload",
            new AiPromptIdentity("generic-analysis", "unknown"),
            "private input",
            new AiStructuredResultIdentity("generic-result", "v1"),
            "trace-123");

        await Assert.ThrowsAsync<AiPromptContractNotFoundException>(() =>
            harness.Subject.ExecuteAsync(command));

        Assert.Equal(0, harness.Provider.CallCount);
        Assert.Null(harness.Repository.Execution);
        Assert.Equal(0, harness.Repository.SaveCount);
    }

    [Fact]
    public void PipelineHasNoSafetyClinicalHistoryOrFhirDependency()
    {
        var dependencies = typeof(ExecuteAiAnalysis).GetConstructors().Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.FullName!)
            .ToArray();

        Assert.DoesNotContain(dependencies, name =>
            name.Contains("Safety", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("History", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Fhir", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Triage", StringComparison.OrdinalIgnoreCase));
    }

    private static ExecuteAiAnalysisCommand Command() => new(
        EntityId.New(),
        "generic-workload",
        new AiPromptIdentity("generic-analysis", "v2"),
        "normalized private input",
        new AiStructuredResultIdentity("generic-result", "v1"),
        "trace-123");

    private sealed class Harness
    {
        private Harness(FakeProvider provider)
        {
            Provider = provider;
            Repository = new FakeRepository();
            Telemetry = new FakeTelemetry();
            Subject = new ExecuteAiAnalysis(
                new AdvancingClock(Utc(10)),
                Repository,
                new AiPromptResolver([new TestPromptContract()]),
                Provider,
                new AiStructuredResultValidator([new TestSchema()]),
                Telemetry);
        }

        public FakeProvider Provider { get; }

        public FakeRepository Repository { get; }

        public FakeTelemetry Telemetry { get; }

        public ExecuteAiAnalysis Subject { get; }

        public static Harness Success() => WithProviderResponse(
            "{\"schemaVersion\":\"v1\",\"answer\":\"informational\"}");

        public static Harness WithProviderResponse(string content) =>
            WithProvider((_, _) => Task.FromResult(new AiProviderResponse(content)));

        public static Harness WithProviderFailure(Exception exception) =>
            WithProvider((_, _) => Task.FromException<AiProviderResponse>(exception));

        public static Harness WithProvider(
            Func<AiProviderRequest, CancellationToken, Task<AiProviderResponse>> execute) =>
            new(new FakeProvider(execute));
    }

    private sealed class FakeProvider(
        Func<AiProviderRequest, CancellationToken, Task<AiProviderResponse>> execute)
        : IAiProvider
    {
        public int CallCount { get; private set; }

        public AiProviderRequest? LastRequest { get; private set; }

        public string ProviderIdentifier => "test-provider";

        public string ModelIdentifier => "model-v1";

        public Task<AiProviderResponse> ExecuteAsync(
            AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return execute(request, cancellationToken);
        }
    }

    private sealed class FakeRepository : IAiExecutionRepository
    {
        public AiExecution? Execution { get; private set; }

        public int SaveCount { get; private set; }

        public CancellationToken LastSaveToken { get; private set; }

        public void Add(AiExecution execution)
        {
            Assert.Null(Execution);
            Execution = execution;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            LastSaveToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTelemetry : IAiExecutionTelemetry
    {
        public int StartedCount { get; private set; }

        public int CompletedCount { get; private set; }

        public void Started(AiExecution execution) => StartedCount++;

        public void Completed(AiExecution execution) => CompletedCount++;
    }

    private sealed class TestPromptContract : IAiPromptContract
    {
        public AiPromptIdentity Identity { get; } = new("generic-analysis", "v2");

        public AiResolvedPrompt Build(string preparedInput) => new(
            Identity,
            "System contract v2",
            $"prepared:{preparedInput}");
    }

    private sealed class TestSchema : IAiStructuredResultSchema
    {
        public AiStructuredResultIdentity Identity { get; } = new("generic-result", "v1");

        public AiStructuralValidationResult Validate(JsonElement result)
        {
            if (!result.TryGetProperty("schemaVersion", out var version) ||
                version.ValueKind != JsonValueKind.String)
            {
                return AiStructuralValidationResult.Invalid(
                    AiStructuralValidationIssue.MissingRequiredField);
            }

            if (version.GetString() != Identity.Version)
            {
                return AiStructuralValidationResult.Invalid(
                    AiStructuralValidationIssue.SchemaVersionMismatch);
            }

            if (!result.TryGetProperty("answer", out var answer))
            {
                return AiStructuralValidationResult.Invalid(
                    AiStructuralValidationIssue.MissingRequiredField);
            }

            return answer.ValueKind == JsonValueKind.String
                ? AiStructuralValidationResult.Valid
                : AiStructuralValidationResult.Invalid(
                    AiStructuralValidationIssue.InvalidFieldType);
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
