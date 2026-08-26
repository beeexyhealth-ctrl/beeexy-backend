using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Triage;

public sealed class StartPreTriageTests
{
    [Fact]
    public async Task AnonymousSupportedPathway_CreatesActiveVersionPinned24HourSession()
    {
        var fixture = new Fixture();

        var result = await fixture.StartAsync(PreTriageCallerMode.Anonymous);

        var session = Assert.Single(fixture.Repository.Added);
        Assert.Equal(PreTriageSessionStatus.Active, result.Status);
        Assert.Equal(ClinicalPathways.AbdominalPain, result.Pathway);
        Assert.Null(result.PatientProfileId);
        Assert.Null(session.PatientProfileId);
        Assert.NotNull(result.AnonymousCapability);
        Assert.NotNull(session.AnonymousCapabilityHash);
        Assert.NotEqual(result.AnonymousCapability, session.AnonymousCapabilityHash!.Value);
        Assert.Equal(fixture.Package.Questionnaire.Id, session.QuestionnaireVersionId);
        Assert.Equal(fixture.Package.Questionnaire.Version, result.QuestionnaireVersion);
        Assert.Equal(fixture.Package.RuleSet.Version, result.RuleSetVersion);
        Assert.Equal(Fixture.Now.AddHours(24), result.ExpiresAt);
        Assert.Equal(1, fixture.Repository.SaveCount);
        Assert.Single(fixture.Audit.CreatedSessionIds);
    }

    [Fact]
    public async Task AuthenticatedRequestWithoutPatientId_UsesCurrentPrimaryProfile()
    {
        var fixture = new Fixture();

        var result = await fixture.StartAsync(PreTriageCallerMode.Authenticated);

        var session = Assert.Single(fixture.Repository.Added);
        Assert.Equal(fixture.Profiles.PrimaryProfile.Id, result.PatientProfileId);
        Assert.Equal(fixture.Profiles.PrimaryProfile.Id, session.PatientProfileId);
        Assert.Null(result.AnonymousCapability);
        Assert.Null(session.AnonymousCapabilityHash);
        Assert.Equal(0, fixture.Capabilities.GenerateCount);
    }

    [Fact]
    public async Task AuthenticatedActiveManager_CanStartForManagedPatient()
    {
        var fixture = new Fixture();
        var managedPatientId = EntityId.New();
        fixture.AuthorizationRepository.Set(
            managedPatientId,
            new PatientAccessAuthorizationLookup(true, EntityId.New()));

        var result = await fixture.StartAsync(
            PreTriageCallerMode.Authenticated,
            managedPatientId);

        Assert.Equal(managedPatientId, result.PatientProfileId);
        Assert.Equal(managedPatientId, Assert.Single(fixture.Repository.Added).PatientProfileId);
    }

    [Fact]
    public async Task AuthenticatedUnauthorizedPatient_IsConcealedAndNotPersisted()
    {
        var fixture = new Fixture();
        var target = EntityId.New();
        fixture.AuthorizationRepository.Set(
            target,
            new PatientAccessAuthorizationLookup(true, null));

        await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.StartAsync(PreTriageCallerMode.Authenticated, target));

        Assert.Empty(fixture.Repository.Added);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Equal(0, fixture.Capabilities.GenerateCount);
    }

    [Fact]
    public async Task AnonymousRequestCannotAttachPatientIdentifier()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<Beeexy.Application.Identity.SessionAuthenticationException>(() =>
            fixture.StartAsync(PreTriageCallerMode.Anonymous, EntityId.New()));

        Assert.Empty(fixture.Repository.Added);
    }

    [Theory]
    [InlineData("RESPIRATORY_SYMPTOMS")]
    [InlineData("BACK_PAIN")]
    public async Task RecognizedUnsupportedPathway_FailsWithoutGeneratingCapability(
        string pathway)
    {
        var fixture = new Fixture();
        fixture.Registry.Resolution = new ClinicalPathwayResolution(
            ClinicalPathwayResolutionStatus.RecognizedButUnsupported,
            ClinicalPathwayCode.Create(pathway),
            null);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.StartAsync(PreTriageCallerMode.Anonymous, pathway: pathway));

        Assert.Equal("pre_triage.pathway_unsupported", exception.Code);
        Assert.Empty(fixture.Repository.Added);
        Assert.Equal(0, fixture.Capabilities.GenerateCount);
    }

    [Fact]
    public async Task UnknownPathway_RemainsDistinctAndDoesNotBorrowDefinition()
    {
        var fixture = new Fixture();
        fixture.Registry.Resolution = new ClinicalPathwayResolution(
            ClinicalPathwayResolutionStatus.Unknown,
            null,
            null);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.StartAsync(
                PreTriageCallerMode.Anonymous,
                pathway: "UNKNOWN_SYMPTOM"));

        Assert.Equal("pre_triage.pathway_unknown", exception.Code);
        Assert.Empty(fixture.Repository.Added);
    }

    [Fact]
    public async Task SupportedPathwayWithoutUsableDefinition_FailsClosed()
    {
        var fixture = new Fixture();
        fixture.Registry.Resolution = new ClinicalPathwayResolution(
            ClinicalPathwayResolutionStatus.Supported,
            ClinicalPathways.AbdominalPain,
            null);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.StartAsync(PreTriageCallerMode.Anonymous));

        Assert.Equal("pre_triage.definition_unavailable", exception.Code);
        Assert.Empty(fixture.Repository.Added);
        Assert.Equal(0, fixture.Capabilities.GenerateCount);
    }

    [Fact]
    public async Task PersistenceFailure_DoesNotReturnCapabilityOrEmitSuccessAudit()
    {
        var fixture = new Fixture();
        fixture.Repository.SaveException = new InvalidOperationException("test-only failure");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.StartAsync(PreTriageCallerMode.Anonymous));

        Assert.Single(fixture.Repository.Added);
        Assert.Equal(1, fixture.Capabilities.GenerateCount);
        Assert.Empty(fixture.Audit.CreatedSessionIds);
    }

    [Fact]
    public async Task UnsupportedRequestField_IsRejectedBeforeClinicalOrSecurityWork()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.StartAsync(
                PreTriageCallerMode.Anonymous,
                unsupportedFields: ["urgency"]));

        Assert.Equal("pre_triage.unsupported_field", exception.Code);
        Assert.Equal(0, fixture.Registry.ResolveCount);
        Assert.Equal(0, fixture.Capabilities.GenerateCount);
    }

    private sealed class Fixture
    {
        public static readonly DateTimeOffset Now =
            new(2026, 8, 22, 4, 0, 0, TimeSpan.Zero);

        public Fixture()
        {
            Package = AbdominalPainProvisionalPackage.Create();
            Registry.Resolution = new ClinicalPathwayResolution(
                ClinicalPathwayResolutionStatus.Supported,
                ClinicalPathways.AbdominalPain,
                Package);
            var authorize = new AuthorizePatientAccess(
                Clock,
                Profiles.Resolver,
                AuthorizationRepository,
                Profiles.MyCircleAudit);
            UseCase = new StartPreTriage(
                Clock,
                Profiles.Resolver,
                authorize,
                Registry,
                Capabilities,
                Repository,
                Audit);
        }

        public ClinicalDefinitionPackage Package { get; }

        public FakeClock Clock { get; } = new();

        public MyCircleListingTestFixture Profiles { get; } = new();

        public FakeAuthorizationRepository AuthorizationRepository { get; } = new();

        public FakeRegistry Registry { get; } = new();

        public TrackingCapabilityService Capabilities { get; } = new();

        public FakeSessionRepository Repository { get; } = new();

        public FakeAuditLogger Audit { get; } = new();

        public StartPreTriage UseCase { get; }

        public Task<StartPreTriageResult> StartAsync(
            PreTriageCallerMode mode,
            EntityId? patientProfileId = null,
            string pathway = "ABDOMINAL_PAIN",
            IReadOnlyCollection<string>? unsupportedFields = null) =>
            UseCase.ExecuteAsync(new StartPreTriageCommand(
                pathway,
                patientProfileId,
                mode,
                unsupportedFields ?? []));
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Fixture.Now;
    }

    private sealed class FakeAuthorizationRepository : IPatientAccessAuthorizationRepository
    {
        private readonly Dictionary<EntityId, PatientAccessAuthorizationLookup> _lookups = [];

        public void Set(EntityId target, PatientAccessAuthorizationLookup lookup) =>
            _lookups[target] = lookup;

        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_lookups.GetValueOrDefault(
                targetProfileId,
                new PatientAccessAuthorizationLookup(false, null)));
    }

    private sealed class FakeRegistry : IClinicalPathwayRegistry
    {
        public ClinicalPathwayResolution Resolution { get; set; } = null!;

        public int ResolveCount { get; private set; }

        public bool IsRecognized(ClinicalPathwayCode pathway) => true;

        public bool IsSupported(ClinicalPathwayCode pathway) => true;

        public Task<ClinicalPathwayResolution> ResolveAsync(
            string pathwayCode,
            CancellationToken cancellationToken = default)
        {
            ResolveCount++;
            return Task.FromResult(Resolution);
        }
    }

    private sealed class TrackingCapabilityService : IAnonymousPreTriageCapabilityService
    {
        private readonly CryptographicAnonymousPreTriageCapabilityService _inner = new();

        public int GenerateCount { get; private set; }

        public GeneratedAnonymousCapability Generate()
        {
            GenerateCount++;
            return _inner.Generate();
        }

        public AnonymousCapabilityHash Hash(string capability) => _inner.Hash(capability);

        public bool Verify(string? capability, AnonymousCapabilityHash expectedHash) =>
            _inner.Verify(capability, expectedHash);
    }

    private sealed class FakeSessionRepository : IPreTriageSessionRepository
    {
        public List<PreTriageSession> Added { get; } = [];

        public int SaveCount { get; private set; }

        public Exception? SaveException { get; set; }

        public void Add(PreTriageSession session) => Added.Add(session);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return SaveException is null
                ? Task.CompletedTask
                : Task.FromException(SaveException);
        }
    }

    private sealed class FakeAuditLogger : IPreTriageSessionAuditLogger
    {
        public List<EntityId> CreatedSessionIds { get; } = [];

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
            DateTimeOffset expiresAt) => CreatedSessionIds.Add(sessionId);

        public void SessionRejected(
            PreTriageCallerMode callerMode,
            string? pathway,
            PreTriageStartRejectionCategory category)
        {
        }
    }
}
