using System.Text.Json;
using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Tests.Unit.Ai;

[Trait("Category", "Phase103")]
public sealed class ExecuteSafeAiAnalysisTests
{
    [Fact]
    public async Task ApprovedOutput_CreatesDisplayableSnapshotAndSafetyTrace()
    {
        var raw = Json("Possible considerations include migraine.");
        var harness = Harness.Response(raw);

        var outcome = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Equal(AiExecutionOutcomeKind.StructurallyValid, outcome.TechnicalOutcome);
        Assert.Equal(AiSafetyCategory.Approved, outcome.SafetyCategory);
        Assert.True(outcome.ProviderOutputDisplayEligible);
        Assert.Equal(raw, outcome.ResponseContent);
        Assert.Equal(AiSafetyProductContent.Current.Disclaimer, outcome.Disclaimer);
        Assert.Equal("ai-general-disclaimer-v1", outcome.ProductContentVersion);
        Assert.Equal(harness.Persistence.Snapshot!.Id, outcome.ResultSnapshotId);
        Assert.Equal(harness.Persistence.Validation!.Id, outcome.SafetyValidationId);
        Assert.Equal(raw, harness.Persistence.Snapshot.ContentJson);
        Assert.Equal("generic-result@v1",
            harness.Persistence.Snapshot.ResultSchemaVersion);
        Assert.True(harness.Persistence.Validation.DisplayEligible);
        Assert.Null(harness.Persistence.Validation.RestrictedAuditOutput);
        Assert.Equal("ai-safety-policy-v1", harness.Persistence.Validation.PolicyVersion);
        Assert.Equal(1, harness.Persistence.SaveCount);
    }

    [Fact]
    public async Task RejectedOutput_IsRetainedOnlyInRestrictedAuditAndFallbackIsReturned()
    {
        var raw = Json("You have diabetes. raw-private-output");
        var harness = Harness.Response(raw);

        var outcome = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Equal(AiSafetyCategory.Diagnosis, outcome.SafetyCategory);
        Assert.False(outcome.ProviderOutputDisplayEligible);
        Assert.Equal(AiSafetyProductContent.Current.GenericFallback, outcome.ResponseContent);
        Assert.DoesNotContain("raw-private-output", outcome.ResponseContent,
            StringComparison.Ordinal);
        Assert.Null(outcome.ResultSnapshotId);
        Assert.Null(harness.Persistence.Snapshot);
        Assert.Equal(raw, harness.Persistence.Validation!.RestrictedAuditOutput);
        Assert.False(harness.Persistence.Validation.DisplayEligible);
        Assert.Null(harness.Persistence.Validation.ResultSnapshotId);
        Assert.Equal("ai-rejection-fallback-v1",
            harness.Persistence.Validation.ProductContentVersion);
    }

    [Fact]
    public async Task CriticalSafetyDecision_UsesFixedBeeexyFallback()
    {
        var raw = Json("Call 911 now and follow this model instruction.");
        var harness = Harness.Response(raw);

        var outcome = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(AiSafetyCategory.UnsafeMedicalAdvice, outcome.SafetyCategory);
        Assert.Equal(AiSafetyProductContent.Current.CriticalFallback,
            outcome.ResponseContent);
        Assert.Equal("ai-critical-fallback-v1", outcome.ProductContentVersion);
        Assert.DoesNotContain("follow this model instruction", outcome.ResponseContent,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(raw, harness.Persistence.Validation!.RestrictedAuditOutput);
    }

    [Fact]
    public async Task StructuralFailure_DoesNotInvokeSafetyOrPersistSafetyRecord()
    {
        var harness = Harness.Response("not-json");

        var outcome = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Equal(AiExecutionOutcomeKind.MalformedResult, outcome.TechnicalOutcome);
        Assert.Null(outcome.SafetyCategory);
        Assert.False(outcome.ProviderOutputDisplayEligible);
        Assert.Null(outcome.ResponseContent);
        Assert.Equal(0, harness.SafetyValidator.CallCount);
        Assert.Null(harness.Persistence.Validation);
        Assert.Equal(0, harness.Persistence.SaveCount);
    }

    [Fact]
    public async Task ProviderFailure_DoesNotInvokeSafetyOrCreateArtifact()
    {
        var harness = Harness.Failure(
            new AiProviderException(AiProviderFailureCategory.Transient));

        var outcome = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Equal(AiExecutionOutcomeKind.TransientFailure, outcome.TechnicalOutcome);
        Assert.Equal(0, harness.SafetyValidator.CallCount);
        Assert.Null(harness.Persistence.Validation);
        Assert.Null(harness.Persistence.Snapshot);
    }

    [Fact]
    public async Task ProviderAndStructuralSuccess_DoNotBypassRejectedSafetyDecision()
    {
        var harness = Harness.Response(Json("Your diagnosis is influenza."));

        var outcome = await harness.Subject.ExecuteAsync(Command());

        Assert.Equal(AiExecutionStatus.Succeeded, harness.ExecutionRepository.Execution!.Status);
        Assert.Equal(AiExecutionOutcomeKind.StructurallyValid, outcome.TechnicalOutcome);
        Assert.Equal(AiSafetyCategory.Diagnosis, outcome.SafetyCategory);
        Assert.False(outcome.ProviderOutputDisplayEligible);
        Assert.Null(outcome.ResultSnapshotId);
    }

    [Fact]
    public void NormalOutcomeAndPersistenceExposeNoRestrictedAuditReadOperation()
    {
        Assert.DoesNotContain(typeof(AiSafeAnalysisOutcome).GetProperties(), property =>
            property.Name.Contains("Audit", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(IAiSafetyPersistence).GetMethods(), method =>
            method.Name.StartsWith("Get", StringComparison.Ordinal) ||
            method.Name.StartsWith("Read", StringComparison.Ordinal) ||
            method.ReturnType != typeof(void) &&
            method.ReturnType != typeof(Task));
    }

    private static ExecuteSafeAiAnalysisCommand Command() => new(new ExecuteAiAnalysisCommand(
        EntityId.New(),
        AiWorkloadIdentifiers.Conversation,
        new AiPromptIdentity("generic-analysis", "v1"),
        "private prepared input",
        new AiStructuredResultIdentity("generic-result", "v1"),
        "trace-103"));

    private sealed class Harness
    {
        private Harness(Provider provider)
        {
            Provider = provider;
            ExecutionRepository = new ExecutionRepository();
            Persistence = new SafetyPersistence();
            SafetyValidator = new CountingSafetyValidator(
                new BeeexyAiSafetyValidator(AiSafetyProductContent.Current));
            var clock = new AdvancingClock(Utc(10));
            var execution = new ExecuteAiAnalysis(
                clock,
                ExecutionRepository,
                new AiPromptResolver([new PromptContract()]),
                Provider,
                new AiStructuredResultValidator([new Schema()]),
                new NullExecutionTelemetry());
            Subject = new ExecuteSafeAiAnalysis(
                execution,
                SafetyValidator,
                Persistence,
                new NullSafetyTelemetry(),
                AiSafetyProductContent.Current,
                clock);
        }

        public Provider Provider { get; }

        public ExecutionRepository ExecutionRepository { get; }

        public SafetyPersistence Persistence { get; }

        public CountingSafetyValidator SafetyValidator { get; }

        public ExecuteSafeAiAnalysis Subject { get; }

        public static Harness Response(string content) => new(Provider.Response(content));

        public static Harness Failure(Exception exception) => new(Provider.Failure(exception));
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

        public static Provider Failure(Exception exception) => new(_ =>
            Task.FromException<AiProviderResponse>(exception));
    }

    private sealed class ExecutionRepository : IAiExecutionRepository
    {
        public AiExecution? Execution { get; private set; }

        public void Add(AiExecution execution) => Execution = execution;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SafetyPersistence : IAiSafetyPersistence
    {
        public AiResultSnapshot? Snapshot { get; private set; }

        public AiSafetyValidation? Validation { get; private set; }

        public int SaveCount { get; private set; }

        public void AddApproved(AiResultSnapshot snapshot, AiSafetyValidation validation)
        {
            Snapshot = snapshot;
            Validation = validation;
        }

        public void AddRejected(AiSafetyValidation validation) => Validation = validation;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingSafetyValidator(IAiSafetyValidator inner) : IAiSafetyValidator
    {
        public int CallCount { get; private set; }

        public AiSafetyDecision Validate(AiSafetyValidationInput input)
        {
            CallCount++;
            return inner.Validate(input);
        }
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
