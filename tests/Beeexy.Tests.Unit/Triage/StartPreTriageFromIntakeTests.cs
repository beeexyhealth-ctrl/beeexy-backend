using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Triage;

public sealed class StartPreTriageFromIntakeTests
{
    [Fact]
    public async Task ResolvedText_StartsNormalSessionAndPersistsValidatedValuesWithOneAiCall()
    {
        var fixture = new Fixture(new FixedAiProvider(Output(
            "ABDOMINAL_PAIN",
            [
                Fact("DURATION", new ClinicalAiDurationValue(2, ClinicalDurationUnit.Days)),
                Fact("INTENSITY", new ClinicalAiIntegerValue(6))
            ])));

        var result = await fixture.UseCase.ExecuteAsync(Command(
            "My stomach has hurt for two days and it is 6 out of 10"));

        Assert.Equal(PreTriageIntakeResolution.Resolved, result.Resolution);
        Assert.Equal(ClinicalPathways.AbdominalPain, result.Session!.Pathway);
        Assert.Equal(PreTriageSessionStatus.Active, result.Session.Status);
        Assert.NotNull(result.Session.AnonymousCapability);
        Assert.Equal(2, result.InitialAnswers!.AcceptedValues.Count);
        Assert.Equal(2, Assert.Single(fixture.Store.Sessions).Answers.Count);
        Assert.Equal(1, fixture.AiProvider.CallCount);
        Assert.Equal(1, fixture.Transaction.CallCount);
        Assert.False(result.InitialAnswers.Progression.ReadyToComplete);
        Assert.Equal(
            QuestionCode.Create("ADDITIONAL_SYMPTOMS"),
            result.InitialAnswers.Progression.NextQuestion!.Code);
    }

    [Fact]
    public async Task ExactAlias_StartsSessionWithoutCallingAi()
    {
        var fixture = new Fixture(new FixedAiProvider(
            Output("HEADACHE"),
            throwWhenCalled: true));

        var result = await fixture.UseCase.ExecuteAsync(Command("Chest pain"));

        Assert.Equal(PreTriageIntakeResolution.Resolved, result.Resolution);
        Assert.Equal(ClinicalPathways.ChestPain, result.Session!.Pathway);
        Assert.Empty(result.InitialAnswers!.AcceptedValues);
        Assert.Equal(0, fixture.AiProvider.CallCount);
        Assert.Single(fixture.Store.Sessions);
    }

    [Fact]
    public async Task AmbiguousText_DoesNotOpenTransactionOrCreateSession()
    {
        var fixture = new Fixture(new FixedAiProvider(Output(
            "HEADACHE",
            symptoms:
            [
                Symptom("head hurts", "HEADACHE"),
                Symptom("chest hurts", "CHEST_PAIN")
            ],
            ambiguities: [new ClinicalAiAmbiguity(ClinicalAiAmbiguityKind.Pathway)],
            intent: ClinicalIntentClassification.Ambiguous,
            requiresClarification: true)));

        var result = await fixture.UseCase.ExecuteAsync(Command(
            "My head and chest both hurt"));

        Assert.Equal(PreTriageIntakeResolution.Ambiguous, result.Resolution);
        Assert.Equal(
            [ClinicalPathways.Headache, ClinicalPathways.ChestPain],
            result.CandidatePathways);
        Assert.Null(result.Session);
        Assert.Null(result.InitialAnswers);
        Assert.Empty(fixture.Store.Sessions);
        Assert.Equal(0, fixture.Transaction.CallCount);
    }

    [Fact]
    public async Task UnresolvedText_DoesNotCreateClinicalState()
    {
        var fixture = new Fixture(new FixedAiProvider(Output(
            "OTHER_SYMPTOMS",
            ambiguities:
            [new ClinicalAiAmbiguity(ClinicalAiAmbiguityKind.InsufficientContext)],
            intent: ClinicalIntentClassification.Ambiguous,
            requiresClarification: true)));

        var result = await fixture.UseCase.ExecuteAsync(Command("I do not know"));

        Assert.Equal(PreTriageIntakeResolution.Unresolved, result.Resolution);
        Assert.Null(result.Session);
        Assert.Empty(fixture.Store.Sessions);
        Assert.Equal(0, fixture.Transaction.CallCount);
    }

    [Fact]
    public async Task ProviderFailure_HappensBeforeTransactionAndSessionCreation()
    {
        var fixture = new Fixture(new FixedAiProvider(
            Output("HEADACHE"),
            ClinicalAiProviderFailureCategory.Unavailable));

        await Assert.ThrowsAsync<PreTriageInterpretationUnavailableException>(() =>
            fixture.UseCase.ExecuteAsync(Command("My head has hurt all morning")));

        Assert.Empty(fixture.Store.Sessions);
        Assert.Equal(0, fixture.Transaction.CallCount);
        Assert.Equal(1, fixture.AiProvider.CallCount);
    }

    private static StartPreTriageFromIntakeCommand Command(string text) => new(
        text,
        PreTriageCallerMode.Anonymous,
        []);

    private static ClinicalAiFactCandidate Fact(
        string code,
        ClinicalAiCandidateValue value) => new(
            QuestionCode.Create(code),
            value,
            ClinicalAiConfidenceSignal.Sufficient);

    private static ClinicalAiSymptomCandidate Symptom(string text, string pathway) => new(
        text,
        pathway,
        ClinicalAiConfidenceSignal.Sufficient);

    private static ClinicalAiProviderOutput Output(
        string pathway,
        IReadOnlyList<ClinicalAiFactCandidate>? facts = null,
        IReadOnlyList<ClinicalAiSymptomCandidate>? symptoms = null,
        IReadOnlyList<ClinicalAiAmbiguity>? ambiguities = null,
        ClinicalIntentClassification intent = ClinicalIntentClassification.PreTriageInput,
        bool requiresClarification = false) => new(
            ClinicalAiProviderOutput.CurrentSchemaVersion,
            intent,
            pathway,
            facts ?? [],
            symptoms ?? [],
            ambiguities ?? [],
            requiresClarification,
            []);

    private sealed class Fixture
    {
        private static readonly DateTimeOffset Now =
            new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

        public Fixture(FixedAiProvider aiProvider)
        {
            AiProvider = aiProvider;
            var definitions = new FakeDefinitionProvider();
            var registry = new ClinicalPathwayRegistry(definitions);
            var profiles = new MyCircleListingTestFixture();
            var clock = new FakeClock(Now);
            var authorization = new AuthorizePatientAccess(
                clock,
                profiles.Resolver,
                new EmptyAccessRepository(),
                profiles.MyCircleAudit);
            var capabilities = new FixedCapabilityService();
            var outputValidator = new ClinicalAiOutputValidator(registry);
            var interpreter = new InterpretPreTriageIntake(
                registry,
                aiProvider,
                new ClinicalSafetyPolicy(),
                outputValidator,
                new NullInterpretationAuditLogger());
            var start = new StartPreTriage(
                clock,
                profiles.Resolver,
                authorization,
                registry,
                capabilities,
                Store,
                new NullSessionAuditLogger());
            var inSessionInterpreter = new InterpretClinicalInput(
                aiProvider,
                new ClinicalSafetyPolicy(),
                outputValidator);
            var submit = new SubmitTriageAnswers(
                clock,
                authorization,
                capabilities,
                Store,
                definitions,
                inSessionInterpreter,
                new NullIntakeAuditLogger());
            UseCase = new StartPreTriageFromIntake(
                interpreter,
                start,
                submit,
                Transaction);
        }

        public FixedAiProvider AiProvider { get; }

        public SessionStore Store { get; } = new();

        public InlineTransaction Transaction { get; } = new();

        public StartPreTriageFromIntake UseCase { get; }
    }

    private sealed class FakeDefinitionProvider : IClinicalDefinitionProvider
    {
        private static readonly IReadOnlyDictionary<ClinicalPathwayCode, ClinicalDefinitionPackage>
            Packages = SimplifiedDemoDefinitionPackages.CreateAll()
                .ToDictionary(value => value.Pathway);

        public Task<ClinicalDefinitionPackage?> GetActiveDefinitionAsync(
            ClinicalPathwayCode pathway,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Packages.GetValueOrDefault(pathway));

        public Task<ClinicalDefinitionPackage?> GetActiveDefinitionAsync(
            ClinicalPathwayCode pathway,
            ClinicalDefinitionPackageProfile profile,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(profile == ClinicalDefinitionPackageProfile.SimplifiedDemoIntake
                ? Packages.GetValueOrDefault(pathway)
                : null);

        public Task<ClinicalDefinitionPackage?> GetDefinitionAsync(
            ClinicalPathwayCode pathway,
            DefinitionVersion version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Packages.TryGetValue(pathway, out var package) &&
                package.Version == version
                    ? package
                    : null);

        public Task<ClinicalDefinitionPackage?> GetDefinitionByQuestionnaireIdAsync(
            EntityId questionnaireVersionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Packages.Values.SingleOrDefault(
                value => value.Questionnaire.Id == questionnaireVersionId));
    }

    private sealed class FixedAiProvider(
        ClinicalAiProviderOutput output,
        ClinicalAiProviderFailureCategory? failure = null,
        bool throwWhenCalled = false) : IClinicalAiProvider
    {
        public int CallCount { get; private set; }

        public Task<ClinicalAiProviderOutput> InterpretAsync(
            ClinicalAiInterpretationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (throwWhenCalled)
            {
                throw new InvalidOperationException("AI must not be invoked.");
            }

            if (failure.HasValue)
            {
                throw new ClinicalAiProviderException(failure.Value);
            }

            return Task.FromResult(output);
        }
    }

    private sealed class SessionStore : IPreTriageSessionRepository, IPreTriageAnswerRepository
    {
        public List<PreTriageSession> Sessions { get; } = [];

        public void Add(PreTriageSession session) => Sessions.Add(session);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PreTriageSession?> GetAsync(
            EntityId sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Sessions.SingleOrDefault(value => value.Id == sessionId));

        public async Task<TResult?> MutateLockedAsync<TResult>(
            EntityId sessionId,
            Func<PreTriageSession, Task<TResult>> mutation,
            CancellationToken cancellationToken = default)
            where TResult : class
        {
            var session = Sessions.SingleOrDefault(value => value.Id == sessionId);
            return session is null ? null : await mutation(session);
        }
    }

    private sealed class InlineTransaction : IPreTriageIntakeOrchestrationTransaction
    {
        public int CallCount { get; private set; }

        public async Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return await operation(cancellationToken);
        }
    }

    private sealed class FixedCapabilityService : IAnonymousPreTriageCapabilityService
    {
        private readonly CryptographicAnonymousPreTriageCapabilityService _inner = new();

        public GeneratedAnonymousCapability Generate() => _inner.Generate();

        public AnonymousCapabilityHash Hash(string capability) => _inner.Hash(capability);

        public bool Verify(string? capability, AnonymousCapabilityHash expectedHash) =>
            capability is not null && _inner.Verify(capability, expectedHash);
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class EmptyAccessRepository : IPatientAccessAuthorizationRepository
    {
        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PatientAccessAuthorizationLookup(false, null));
    }

    private sealed class NullInterpretationAuditLogger : IPreTriageInterpretationAuditLogger
    {
        public void InterpretationEvaluated(
            PreTriageIntakeResolution resolution,
            ClinicalPathwayCode? pathway,
            bool usedAi,
            int acceptedCandidateCategoryCount)
        {
        }

        public void InterpretationFailed(ClinicalAiProviderFailureCategory failure)
        {
        }
    }

    private sealed class NullSessionAuditLogger : IPreTriageSessionAuditLogger
    {
        public void SessionCreated(
            EntityId sessionId,
            PreTriageCallerMode callerMode,
            ClinicalPathwayCode pathway,
            QuestionnaireCode questionnaireCode,
            DefinitionVersion questionnaireVersion,
            RuleSetCode ruleSetCode,
            DefinitionVersion ruleSetVersion,
            EntityId? patientProfileId,
            DateTimeOffset createdAt,
            DateTimeOffset expiresAt)
        {
        }

        public void SessionRejected(
            PreTriageCallerMode callerMode,
            string? pathway,
            PreTriageStartRejectionCategory category)
        {
        }
    }

    private sealed class NullIntakeAuditLogger : IPreTriageIntakeAuditLogger
    {
        public void InterpretationEvaluated(
            EntityId sessionId,
            bool usedNaturalLanguage,
            TriageIntakeSubmissionOutcome outcome,
            int acceptedCandidateCategoryCount)
        {
        }

        public void AnswersProcessed(
            EntityId sessionId,
            TriageIntakeSubmissionOutcome outcome,
            int acceptedAnswerCategoryCount,
            bool readyToComplete)
        {
        }
    }
}
