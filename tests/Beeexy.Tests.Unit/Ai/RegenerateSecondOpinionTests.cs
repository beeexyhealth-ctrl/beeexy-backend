using System.Text.Json;
using Beeexy.Application.Ai;
using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Ai;

[Trait("Category", "Phase107")]
public sealed class RegenerateSecondOpinionTests
{
    [Fact]
    public async Task ApprovedRegeneration_ReplaysExactInputAndCreatesNewExecutionAndSnapshot()
    {
        var harness = Harness.Create();
        var originalExecutionId = EntityId.New();

        var receipt = await harness.Subject.ExecuteAsync(harness.Command());

        Assert.Equal(SecondOpinionStatus.Succeeded, receipt.Status);
        Assert.NotEqual(originalExecutionId, receipt.ExecutionId);
        Assert.Equal(1, harness.Provider.CallCount);
        var providerRequest = Assert.Single(harness.Provider.Requests);
        Assert.Equal(harness.ExpectedProviderInput, providerRequest.UserContent);
        Assert.Equal(SecondOpinionContract.Prompt, providerRequest.Prompt);
        var execution = Assert.Single(harness.Executions.Executions);
        Assert.Equal(receipt.ExecutionId, execution.Id);
        Assert.Equal(AiExecutionStatus.Succeeded, execution.Status);
        Assert.Equal("phase-107-provider", execution.ProviderIdentifier);
        Assert.Equal("phase-107-model", execution.ModelIdentifier);
        Assert.Equal("ai-second-opinion@v1", execution.PromptVersion);
        var snapshot = Assert.Single(harness.Safety.Snapshots);
        Assert.Equal(harness.AnalysisId, snapshot.AnalysisRequestId);
        Assert.Equal(receipt.ExecutionId, snapshot.ExecutionId);
        Assert.Equal(2, snapshot.Sequence);
        Assert.True(Assert.Single(harness.Safety.Validations).DisplayEligible);
    }

    [Fact]
    public async Task RepeatedApprovedRegeneration_AppendsDistinctMonotonicSnapshots()
    {
        var harness = Harness.Create();

        var second = await harness.Subject.ExecuteAsync(harness.Command());
        harness.Repository.NextSnapshotSequence = 3;
        var third = await harness.Subject.ExecuteAsync(harness.Command());

        Assert.NotEqual(second.ExecutionId, third.ExecutionId);
        Assert.Equal(2, harness.Provider.CallCount);
        Assert.Equal([2, 3], harness.Safety.Snapshots.Select(item => item.Sequence));
        Assert.Equal(2, harness.Safety.Snapshots.Select(item => item.Id).Distinct().Count());
        Assert.All(harness.Safety.Snapshots,
            item => Assert.Equal(harness.AnalysisId, item.AnalysisRequestId));
    }

    [Fact]
    public async Task MissingAnalysis_IsConcealedBeforeLeaseAndProvider()
    {
        var harness = Harness.Create();
        harness.Repository.Owned = false;

        await Assert.ThrowsAsync<SecondOpinionNotFoundException>(
            () => harness.Subject.ExecuteAsync(harness.Command()));

        Assert.Equal(0, harness.Repository.LeaseAttempts);
        Assert.Equal(0, harness.Provider.CallCount);
        Assert.Empty(harness.Executions.Executions);
    }

    [Fact]
    public async Task RevokedPatientAuthority_IsConcealedBeforeLeaseAndProvider()
    {
        var harness = Harness.Create(patientId: EntityId.New());

        await Assert.ThrowsAsync<SecondOpinionNotFoundException>(
            () => harness.Subject.ExecuteAsync(harness.Command()));

        Assert.Equal(0, harness.Repository.LeaseAttempts);
        Assert.Equal(0, harness.Provider.CallCount);
        Assert.Empty(harness.Executions.Executions);
    }

    [Fact]
    public async Task ActiveExecutionConflict_MakesZeroProviderCallsAndCreatesNoExecution()
    {
        var harness = Harness.Create();
        harness.Repository.LeaseAvailable = false;

        await Assert.ThrowsAsync<SecondOpinionExecutionConflictException>(
            () => harness.Subject.ExecuteAsync(harness.Command()));

        Assert.Equal(1, harness.Repository.LeaseAttempts);
        Assert.Equal(0, harness.Provider.CallCount);
        Assert.Empty(harness.Executions.Executions);
        Assert.Empty(harness.Safety.Snapshots);
    }

    [Theory]
    [MemberData(nameof(InvalidImmutableInputs))]
    public async Task InvalidImmutableInput_Returns422BeforeExecutionOrProvider(
        string schemaVersion,
        string snapshot)
    {
        var harness = Harness.Create();
        harness.Repository.SchemaVersion = schemaVersion;
        harness.Repository.ImmutableInput = snapshot;

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => harness.Subject.ExecuteAsync(harness.Command()));

        Assert.Equal("ai.second_opinion.immutable_input_invalid", exception.Code);
        Assert.Equal(0, harness.Provider.CallCount);
        Assert.Empty(harness.Executions.Executions);
        Assert.Empty(harness.Safety.Snapshots);
    }

    [Theory]
    [InlineData(FailureMode.Timeout, SecondOpinionStatus.Failed, AiExecutionStatus.Failed)]
    [InlineData(FailureMode.Transient, SecondOpinionStatus.Failed, AiExecutionStatus.Failed)]
    [InlineData(FailureMode.Permanent, SecondOpinionStatus.Failed, AiExecutionStatus.Failed)]
    [InlineData(FailureMode.Malformed, SecondOpinionStatus.Rejected, AiExecutionStatus.Rejected)]
    [InlineData(FailureMode.Unsafe, SecondOpinionStatus.Rejected, AiExecutionStatus.Succeeded)]
    [InlineData(FailureMode.Cancelled, SecondOpinionStatus.Failed, AiExecutionStatus.Failed)]
    public async Task FailedRegeneration_PreservesPriorSnapshotAndTrace(
        FailureMode mode,
        SecondOpinionStatus expectedStatus,
        AiExecutionStatus expectedExecutionStatus)
    {
        var harness = Harness.Create(mode);
        var prior = harness.Safety.SeedPrior(harness.AnalysisId);
        var priorJson = prior.ContentJson;
        var cancellation = new CancellationTokenSource();
        if (mode == FailureMode.Cancelled)
        {
            cancellation.Cancel();
        }

        var receipt = await harness.Subject.ExecuteAsync(
            harness.Command(),
            cancellation.Token);

        Assert.Equal(expectedStatus, receipt.Status);
        Assert.Equal(1, harness.Provider.CallCount);
        Assert.Single(harness.Safety.Snapshots);
        Assert.Same(prior, harness.Safety.Snapshots[0]);
        Assert.Equal(priorJson, prior.ContentJson);
        Assert.Equal(expectedExecutionStatus, Assert.Single(harness.Executions.Executions).Status);
        if (mode == FailureMode.Unsafe)
        {
            var validation = Assert.Single(harness.Safety.Validations);
            Assert.False(validation.DisplayEligible);
            Assert.Null(validation.ResultSnapshotId);
        }
    }

    [Fact]
    public async Task GetAfterFailedAttempt_ReturnsTheStoredApprovedSnapshotProvenance()
    {
        var harness = Harness.Create();
        harness.Repository.State = new SecondOpinionStoredState(
            AiExecutionStatus.Succeeded,
            EntityId.New(),
            "snapshot-provider",
            "snapshot-model",
            "ai-second-opinion@v1",
            ApprovedJson("Prior approved summary."),
            harness.Clock.UtcNow.AddMinutes(-1),
            AiSafetyCategory.Approved,
            true,
            SecondOpinionProductContent.DisclaimerVersion);
        var get = new GetSecondOpinion(
            harness.Identity,
            harness.Authorizer,
            harness.Repository,
            AiSafetyProductContent.Current);

        var detail = await get.ExecuteAsync(harness.AnalysisId);

        Assert.Equal(SecondOpinionStatus.Succeeded, detail.Status);
        Assert.Equal("Prior approved summary.", detail.Result!.Summary);
        Assert.Equal("snapshot-provider", detail.Metadata!.Provider);
        Assert.Equal("snapshot-model", detail.Metadata.ModelVersion);
        Assert.Equal("ai-second-opinion@v1", detail.Metadata.PromptVersion);
    }

    public static TheoryData<string, string> InvalidImmutableInputs()
    {
        var patientId = EntityId.New();
        var valid = ImmutableInput(patientId);
        return new TheoryData<string, string>
        {
            { "unsupported", valid.Snapshot },
            { SecondOpinionImmutableInput.SchemaVersion, "not-json" },
            { SecondOpinionImmutableInput.SchemaVersion, "{}" },
            { SecondOpinionImmutableInput.SchemaVersion,
                "{\"schemaVersion\":\"v2\",\"input\":{},\"provenance\":{}}" },
            { SecondOpinionImmutableInput.SchemaVersion,
                "{\"schemaVersion\":\"v1\",\"input\":{},\"provenance\":{}}" },
            { SecondOpinionImmutableInput.SchemaVersion,
                EmptyMedicalInput(EntityId.New()) },
            { SecondOpinionImmutableInput.SchemaVersion,
                MismatchedDocumentInput(EntityId.New()) },
            { SecondOpinionImmutableInput.SchemaVersion,
                valid.Snapshot.Replace(
                    "\"schemaVersion\":\"v1\"",
                    "\"schemaVersion\":\"v1\",\"unexpected\":true",
                    StringComparison.Ordinal) },
            { SecondOpinionImmutableInput.SchemaVersion,
                valid.Snapshot.Replace(
                    "original-user-text-marker",
                    "   ",
                    StringComparison.Ordinal) },
            { SecondOpinionImmutableInput.SchemaVersion,
                valid.Snapshot.Replace("text/plain", "image/png", StringComparison.Ordinal) },
            { SecondOpinionImmutableInput.SchemaVersion,
                valid.Snapshot.Replace(
                    patientId.Value.ToString("D"),
                    Guid.Empty.ToString("D"),
                    StringComparison.Ordinal) }
        };
    }

    public enum FailureMode
    {
        Approved,
        Timeout,
        Transient,
        Permanent,
        Malformed,
        Unsafe,
        Cancelled
    }

    private sealed class Harness
    {
        private Harness(FailureMode mode, EntityId? patientId)
        {
            Clock = new TestClock();
            Profiles = new MyCircleListingTestFixture();
            AccountId = Profiles.Account.Id;
            PatientId = patientId ?? Profiles.PrimaryProfile.Id;
            AnalysisId = EntityId.New();
            Identity = new CurrentIdentity(AccountId);
            Authorizer = new AuthorizePatientAccess(
                Clock,
                Profiles.Resolver,
                new DeniedPatientAccessRepository(),
                Profiles.MyCircleAudit);
            var immutableInput = ImmutableInput(PatientId);
            ExpectedProviderInput = immutableInput.ProviderInput;
            Repository = new Repository(
                AnalysisId,
                AccountId,
                PatientId,
                immutableInput.Snapshot);
            Provider = new Provider(Behavior(mode));
            Executions = new ExecutionRepository();
            Safety = new SafetyPersistence();
            var pipeline = new ExecuteAiAnalysis(
                Clock,
                Executions,
                new AiPromptResolver([new SecondOpinionPromptV1()]),
                Provider,
                new AiStructuredResultValidator([new SecondOpinionResultSchemaV1()]),
                new NullExecutionTelemetry());
            var safe = new ExecuteSafeAiAnalysis(
                pipeline,
                new BeeexyAiSafetyValidator(AiSafetyProductContent.Current),
                Safety,
                new NullSafetyTelemetry(),
                AiSafetyProductContent.Current,
                Clock);
            Subject = new RegenerateSecondOpinion(
                Identity,
                Authorizer,
                Repository,
                safe);
        }

        public EntityId AccountId { get; }
        public EntityId PatientId { get; }
        public EntityId AnalysisId { get; }
        public TestClock Clock { get; }
        public MyCircleListingTestFixture Profiles { get; }
        public CurrentIdentity Identity { get; }
        public AuthorizePatientAccess Authorizer { get; }
        public Repository Repository { get; }
        public Provider Provider { get; }
        public ExecutionRepository Executions { get; }
        public SafetyPersistence Safety { get; }
        public RegenerateSecondOpinion Subject { get; }
        public string ExpectedProviderInput { get; }

        public static Harness Create(
            FailureMode mode = FailureMode.Approved,
            EntityId? patientId = null) => new(mode, patientId);

        public RegenerateSecondOpinionCommand Command() => new(
            AnalysisId,
            "phase-107-correlation");

        private static Func<CancellationToken, Task<AiProviderResponse>> Behavior(
            FailureMode mode) => mode switch
            {
                FailureMode.Approved => _ => Task.FromResult(
                    new AiProviderResponse(ApprovedJson("Regenerated summary."))),
                FailureMode.Timeout => _ => Task.FromException<AiProviderResponse>(
                    new AiProviderException(AiProviderFailureCategory.Timeout)),
                FailureMode.Transient => _ => Task.FromException<AiProviderResponse>(
                    new AiProviderException(AiProviderFailureCategory.Transient)),
                FailureMode.Permanent => _ => Task.FromException<AiProviderResponse>(
                    new AiProviderException(AiProviderFailureCategory.Permanent)),
                FailureMode.Malformed => _ => Task.FromResult(new AiProviderResponse("not-json")),
                FailureMode.Unsafe => _ => Task.FromResult(
                    new AiProviderResponse(ApprovedJson("You have diabetes."))),
                FailureMode.Cancelled => token => Task.FromException<AiProviderResponse>(
                    new OperationCanceledException(token)),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
    }

    private sealed class Repository(
        EntityId analysisId,
        EntityId accountId,
        EntityId patientId,
        string immutableInput) : ISecondOpinionRepository
    {
        public bool Owned { get; set; } = true;
        public bool LeaseAvailable { get; set; } = true;
        public int LeaseAttempts { get; private set; }
        public int NextSnapshotSequence { get; set; } = 2;
        public string SchemaVersion { get; set; } = SecondOpinionImmutableInput.SchemaVersion;
        public string ImmutableInput { get; set; } = immutableInput;
        public SecondOpinionStoredState State { get; set; } = new(
            null, null, null, null, null, null, null, null, null, null);

        public void Add(AiAnalysisRequest request) => throw new NotSupportedException();

        public Task<SecondOpinionAnalysisAccess?> FindOwnedAsync(
            EntityId requestedAnalysisId,
            EntityId requestedAccountId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            Owned && requestedAnalysisId == analysisId && requestedAccountId == accountId
                ? new SecondOpinionAnalysisAccess(analysisId, patientId)
                : null);

        public Task<SecondOpinionRegenerationSource?> FindRegenerationSourceAsync(
            EntityId requestedAnalysisId,
            EntityId requestedAccountId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            Owned && requestedAnalysisId == analysisId && requestedAccountId == accountId
                ? new SecondOpinionRegenerationSource(
                    analysisId,
                    patientId,
                    SchemaVersion,
                    ImmutableInput,
                    NextSnapshotSequence)
                : null);

        public Task<ISecondOpinionExecutionLease?> TryAcquireExecutionLeaseAsync(
            EntityId requestedAnalysisId,
            CancellationToken cancellationToken = default)
        {
            LeaseAttempts++;
            return Task.FromResult<ISecondOpinionExecutionLease?>(
                LeaseAvailable ? new ExecutionLease() : null);
        }

        public Task<SecondOpinionStoredState> GetStateAsync(
            EntityId requestedAnalysisId,
            CancellationToken cancellationToken = default) => Task.FromResult(State);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        private sealed class ExecutionLease : ISecondOpinionExecutionLease
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Provider(
        Func<CancellationToken, Task<AiProviderResponse>> response) : IAiProvider
    {
        public int CallCount { get; private set; }
        public string ProviderIdentifier => "phase-107-provider";
        public string ModelIdentifier => "phase-107-model";
        public List<AiProviderRequest> Requests { get; } = [];

        public Task<AiProviderResponse> ExecuteAsync(
            AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Requests.Add(request);
            return response(cancellationToken);
        }
    }

    private sealed class ExecutionRepository : IAiExecutionRepository
    {
        public List<AiExecution> Executions { get; } = [];
        public void Add(AiExecution execution) => Executions.Add(execution);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SafetyPersistence : IAiSafetyPersistence
    {
        public List<AiResultSnapshot> Snapshots { get; } = [];
        public List<AiSafetyValidation> Validations { get; } = [];

        public AiResultSnapshot SeedPrior(EntityId analysisId)
        {
            var snapshot = AiResultSnapshot.Create(
                analysisId,
                EntityId.New(),
                1,
                SecondOpinionProductContent.ResultVersion,
                ApprovedJson("Original summary."),
                new DateTimeOffset(2026, 9, 2, 11, 59, 0, TimeSpan.Zero));
            Snapshots.Add(snapshot);
            return snapshot;
        }

        public void AddApproved(AiResultSnapshot snapshot, AiSafetyValidation validation)
        {
            Snapshots.Add(snapshot);
            Validations.Add(validation);
        }

        public void AddRejected(AiSafetyValidation validation) => Validations.Add(validation);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CurrentIdentity(EntityId accountId) : ICurrentSessionIdentity
    {
        public CurrentSessionIdentity GetRequired() => new(accountId, EntityId.New());
    }

    private sealed class DeniedPatientAccessRepository : IPatientAccessAuthorizationRepository
    {
        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            new PatientAccessAuthorizationLookup(true, null));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
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

    private static (string ProviderInput, string Snapshot) ImmutableInput(EntityId patientId)
    {
        var documentId = EntityId.New();
        var preTriageId = EntityId.New();
        var historyId = EntityId.New();
        var input = new
        {
            demographics = new { age = (int?)42, sexAssignedAtBirth = "Female" },
            typedText = "original-user-text-marker",
            document = new
            {
                ContentType = "text/plain",
                text = "original-document-text-marker"
            },
            preTriage = new { marker = "original-pre-triage-marker" },
            clinicalHistory = new[] { new { marker = "original-history-marker" } }
        };
        return (
            JsonSerializer.Serialize(input),
            JsonSerializer.Serialize(new
            {
                schemaVersion = "v1",
                input,
                provenance = new
                {
                    patientId = patientId.Value,
                    documentId = documentId.Value,
                    preTriageSessionId = preTriageId.Value,
                    clinicalHistoryEventIds = new[] { historyId.Value }
                }
            }));
    }

    private static string EmptyMedicalInput(EntityId patientId) => JsonSerializer.Serialize(new
    {
        schemaVersion = "v1",
        input = new
        {
            demographics = new { age = (int?)42, sexAssignedAtBirth = "Female" },
            typedText = (string?)null,
            document = (object?)null,
            preTriage = (object?)null,
            clinicalHistory = Array.Empty<object>()
        },
        provenance = new
        {
            patientId = patientId.Value,
            documentId = (Guid?)null,
            preTriageSessionId = (Guid?)null,
            clinicalHistoryEventIds = Array.Empty<Guid>()
        }
    });

    private static string MismatchedDocumentInput(EntityId patientId) => JsonSerializer.Serialize(
        new
        {
            schemaVersion = "v1",
            input = new
            {
                demographics = new { age = (int?)42, sexAssignedAtBirth = "Female" },
                typedText = "original text",
                document = new { ContentType = "text/plain", text = "document text" },
                preTriage = (object?)null,
                clinicalHistory = Array.Empty<object>()
            },
            provenance = new
            {
                patientId = patientId.Value,
                documentId = (Guid?)null,
                preTriageSessionId = (Guid?)null,
                clinicalHistoryEventIds = Array.Empty<Guid>()
            }
        });

    private static string ApprovedJson(string summary) => JsonSerializer.Serialize(new
    {
        schemaVersion = "v1",
        summary,
        importantPoints = new[] { "Important point" },
        possibleQuestionsForDoctor = new[] { "What should I discuss?" },
        missingInformation = new[] { "Additional clinical context" },
        disclaimer = SecondOpinionProductContent.Disclaimer
    });
}
