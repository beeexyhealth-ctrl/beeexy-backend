using System.Text.Json;
using Beeexy.Application.Ai;
using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Tests.Unit.Ai;

[Trait("Category", "Phase106")]
public sealed class SecondOpinionUseCasesTests
{
    [Fact]
    public async Task ApprovedRequest_UsesExactlyOneCallAndPersistsImmutableApprovedResult()
    {
        var harness = Harness.Approved("Possible causes could include dehydration.");

        var result = await harness.Subject.ExecuteAsync(harness.Command());

        Assert.Equal(SecondOpinionStatus.Succeeded, result.Status);
        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Equal(AiAnalysisPurpose.SecondOpinion, harness.Requests.Request!.Purpose);
        Assert.Equal("ai-second-opinion-input@v1",
            harness.Requests.Request.OriginalInputSchemaVersion);
        Assert.Contains("immutable-marker",
            harness.Requests.Request.OriginalInputSnapshotJson,
            StringComparison.Ordinal);
        Assert.Equal("ai-second-opinion@v1", harness.Executions.Execution!.PromptVersion);
        Assert.Equal(AiExecutionStatus.Succeeded, harness.Executions.Execution.Status);
        Assert.NotNull(harness.Safety.Snapshot);
        Assert.True(harness.Safety.Validation!.DisplayEligible);
        Assert.Equal(SecondOpinionProductContent.DisclaimerVersion,
            harness.Safety.Validation.ProductContentVersion);
    }

    [Fact]
    public async Task SelectedDocument_IsAssociatedWithoutChangingItsExpiry()
    {
        var harness = Harness.Approved("Educational summary.", includeDocument: true);
        var expiry = harness.Assembler.Document!.ExpiresAt;

        await harness.Subject.ExecuteAsync(harness.Command());

        Assert.Equal(harness.Requests.Request!.Id,
            harness.Assembler.Document.AnalysisRequestId);
        Assert.Equal(expiry, harness.Assembler.Document.ExpiresAt);
        Assert.Equal(1, harness.Provider.CallCount);
    }

    [Fact]
    public async Task SafetyRejectedRequest_PersistsNoDisplaySnapshotAndReturnsRejectedStatus()
    {
        var harness = Harness.Approved("You have diabetes. restricted-marker");

        var result = await harness.Subject.ExecuteAsync(harness.Command());

        Assert.Equal(SecondOpinionStatus.Rejected, result.Status);
        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Null(harness.Safety.Snapshot);
        Assert.False(harness.Safety.Validation!.DisplayEligible);
        Assert.Contains("restricted-marker",
            harness.Safety.Validation.RestrictedAuditOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedProviderResult_IsRejectedWithoutDisplaySnapshot()
    {
        var harness = Harness.Raw("not-json");

        var result = await harness.Subject.ExecuteAsync(harness.Command());

        Assert.Equal(SecondOpinionStatus.Rejected, result.Status);
        Assert.Equal(AiExecutionStatus.Rejected, harness.Executions.Execution!.Status);
        Assert.Null(harness.Safety.Snapshot);
        Assert.Null(harness.Safety.Validation);
    }

    [Theory]
    [InlineData(AiProviderFailureCategory.Timeout)]
    [InlineData(AiProviderFailureCategory.Transient)]
    [InlineData(AiProviderFailureCategory.Permanent)]
    [InlineData(AiProviderFailureCategory.ConfigurationUnavailable)]
    public async Task ProviderFailure_IsTraceableWithoutResult(
        AiProviderFailureCategory category)
    {
        var harness = Harness.Failure(category);

        var result = await harness.Subject.ExecuteAsync(harness.Command());

        Assert.Equal(SecondOpinionStatus.Failed, result.Status);
        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Equal(AiExecutionStatus.Failed, harness.Executions.Execution!.Status);
        Assert.Null(harness.Safety.Snapshot);
    }

    [Theory]
    [InlineData("ai.second_opinion.input_required")]
    [InlineData("ai.second_opinion.document_limit")]
    [InlineData("ai.second_opinion.document_unavailable")]
    public async Task PreExecutionValidationFailure_MakesZeroProviderCallsAndPersistsNothing(
        string code)
    {
        var harness = Harness.AssemblyFailure(code);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => harness.Subject.ExecuteAsync(harness.Command()));

        Assert.Equal(code, exception.Code);
        Assert.Equal(0, harness.Provider.CallCount);
        Assert.Null(harness.Requests.Request);
        Assert.Null(harness.Executions.Execution);
    }

    [Fact]
    public void PublicReadModels_ExposeOnlySafeResultAndVersionMetadata()
    {
        var names = typeof(SecondOpinionDetail).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Contains(nameof(SecondOpinionDetail.Result), names);
        Assert.Contains(nameof(SecondOpinionDetail.Metadata), names);
        Assert.DoesNotContain(names, name =>
            name.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Restricted", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("OriginalInput", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Harness
    {
        private Harness(
            Func<AiProviderRequest, Task<AiProviderResponse>> response,
            bool includeDocument,
            string? assemblyFailure)
        {
            Clock = new TestClock();
            AccountId = EntityId.New();
            PatientId = EntityId.New();
            Identity = new CurrentIdentity(AccountId);
            Assembler = new InputAssembler(includeDocument, AccountId, assemblyFailure, Clock.UtcNow);
            Requests = new RequestRepository();
            Provider = new Provider(response);
            Executions = new ExecutionRepository();
            Safety = new SafetyPersistence();
            var execution = new ExecuteAiAnalysis(
                Clock,
                Executions,
                new AiPromptResolver([new SecondOpinionPromptV1()]),
                Provider,
                new AiStructuredResultValidator([new SecondOpinionResultSchemaV1()]),
                new NullExecutionTelemetry());
            var safe = new ExecuteSafeAiAnalysis(
                execution,
                new BeeexyAiSafetyValidator(AiSafetyProductContent.Current),
                Safety,
                new NullSafetyTelemetry(),
                AiSafetyProductContent.Current,
                Clock);
            Subject = new RequestSecondOpinion(
                Identity,
                Assembler,
                Requests,
                safe,
                Clock);
        }

        public EntityId AccountId { get; }
        public EntityId PatientId { get; }
        public TestClock Clock { get; }
        public CurrentIdentity Identity { get; }
        public InputAssembler Assembler { get; }
        public RequestRepository Requests { get; }
        public Provider Provider { get; }
        public ExecutionRepository Executions { get; }
        public SafetyPersistence Safety { get; }
        public RequestSecondOpinion Subject { get; }

        public RequestSecondOpinionCommand Command() => new(
            PatientId,
            "Please explain the supplied health information.",
            null,
            null,
            null,
            "phase-106-unit");

        public static Harness Approved(string summary, bool includeDocument = false) =>
            Raw(JsonSerializer.Serialize(new
            {
                schemaVersion = "v1",
                summary,
                importantPoints = new[] { "Point" },
                possibleQuestionsForDoctor = new[] { "Question?" },
                missingInformation = new[] { "More context" },
                disclaimer = SecondOpinionProductContent.Disclaimer
            }), includeDocument);

        public static Harness Raw(string content, bool includeDocument = false) => new(
            _ => Task.FromResult(new AiProviderResponse(content)),
            includeDocument,
            null);

        public static Harness Failure(AiProviderFailureCategory category) => new(
            _ => Task.FromException<AiProviderResponse>(new AiProviderException(category)),
            false,
            null);

        public static Harness AssemblyFailure(string code) => new(
            _ => Task.FromResult(new AiProviderResponse("{}")),
            false,
            code);
    }

    private sealed class InputAssembler : ISecondOpinionInputAssembler
    {
        private readonly string? failure;

        public InputAssembler(
            bool includeDocument,
            EntityId accountId,
            string? failure,
            DateTimeOffset now)
        {
            this.failure = failure;
            if (includeDocument)
            {
                Document = AiUploadedDocument.Create(
                    accountId,
                    new string('a', 64),
                    "text/plain",
                    10,
                    now,
                    now.AddHours(24));
            }
        }

        public AiUploadedDocument? Document { get; }

        public Task<SecondOpinionPreparedInput> AssembleAsync(
            RequestSecondOpinionCommand command,
            EntityId accountId,
            CancellationToken cancellationToken = default)
        {
            if (failure is not null)
            {
                throw new RequestValidationException(failure, "invalid input");
            }

            return Task.FromResult(new SecondOpinionPreparedInput(
                "{\"typedText\":\"provider-only-marker\"}",
                "{\"schemaVersion\":\"v1\",\"input\":\"immutable-marker\"}",
                Document));
        }
    }

    private sealed class CurrentIdentity(EntityId accountId) : ICurrentSessionIdentity
    {
        public CurrentSessionIdentity GetRequired() => new(accountId, EntityId.New());
    }

    private sealed class RequestRepository : ISecondOpinionRepository
    {
        public AiAnalysisRequest? Request { get; private set; }
        public void Add(AiAnalysisRequest request) => Request = request;
        public Task<SecondOpinionAnalysisAccess?> FindOwnedAsync(
            EntityId analysisId,
            EntityId accountId,
            CancellationToken cancellationToken = default) => Task.FromResult(
                Request is null || Request.PatientProfileId is not { } patientId
                    ? null
                    : new SecondOpinionAnalysisAccess(Request.Id, patientId));
        public Task<SecondOpinionRegenerationSource?> FindRegenerationSourceAsync(
            EntityId analysisId,
            EntityId accountId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ISecondOpinionExecutionLease?> TryAcquireExecutionLeaseAsync(
            EntityId analysisId,
            CancellationToken cancellationToken = default) => Task.FromResult<ISecondOpinionExecutionLease?>(
                new ExecutionLease());
        public Task<SecondOpinionStoredState> GetStateAsync(
            EntityId analysisId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        private sealed class ExecutionLease : ISecondOpinionExecutionLease
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Provider(
        Func<AiProviderRequest, Task<AiProviderResponse>> response) : IAiProvider
    {
        public int CallCount { get; private set; }
        public string ProviderIdentifier => "unit-provider";
        public string ModelIdentifier => "unit-model-v1";
        public Task<AiProviderResponse> ExecuteAsync(
            AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return response(request);
        }
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
        public void AddApproved(AiResultSnapshot snapshot, AiSafetyValidation validation)
        {
            Snapshot = snapshot;
            Validation = validation;
        }
        public void AddRejected(AiSafetyValidation validation) => Validation = validation;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NullExecutionTelemetry : IAiExecutionTelemetry
    {
        public void Started(AiExecution execution) { }
        public void Completed(AiExecution execution) { }
    }

    private sealed class NullSafetyTelemetry : IAiSafetyTelemetry
    {
        public void DecisionPersisted(AiSafetyValidation validation) { }
    }

    public sealed class TestClock : IClock
    {
        private DateTimeOffset current = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow
        {
            get
            {
                var result = current;
                current = current.AddSeconds(1);
                return result;
            }
        }
    }
}
