using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Interoperability;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class PreTriageIntakeOrchestrationEndpointTests(
    PostgreSqlContainerFixture postgres)
{
    private const string Endpoint = "/api/v1/pre-triage/intake";
    private const string CapabilityHeader = "X-Pre-Triage-Capability";
    private const string SigningKey =
        "integration-test-only-jwt-signing-key-with-at-least-32-bytes";
    private const string Issuer = "https://api.beeexy.com";
    private const string Audience = "beeexy-client";

    [Fact]
    public async Task ResolvedAnonymousIntake_AtomicallyCreatesPinnedSessionAndAnswersOnce()
    {
        var provider = new FixedProvider(Output(
            "ABDOMINAL_PAIN",
            [
                Fact("DURATION", new ClinicalAiDurationValue(2, ClinicalDurationUnit.Days)),
                Fact("INTENSITY", new ClinicalAiIntegerValue(6))
            ]));
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();
        var before = await CountsAsync();

        using var response = await client.PostAsJsonAsync(Endpoint, new
        {
            text = "My stomach has hurt for two days and it is 6 out of 10"
        });
        var result = await response.Content.ReadFromJsonAsync<OrchestrationResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("RESOLVED", result!.Resolution);
        Assert.Equal("ABDOMINAL_PAIN", result.Session!.Pathway);
        Assert.Equal("Active", result.Session.Status);
        Assert.Null(result.Session.PatientId);
        Assert.False(string.IsNullOrWhiteSpace(result.Session.AnonymousCapability));
        Assert.Equal(result.Session.SessionId, result.InitialAnswers!.SessionId);
        Assert.Equal(["DURATION", "INTENSITY"], result.InitialAnswers.AcceptedAnswers);
        Assert.Equal(new DurationResponse(2, "DAYS"),
            result.InitialAnswers.AcceptedValues.Duration);
        Assert.Equal(6, result.InitialAnswers.AcceptedValues.Intensity);
        Assert.Equal("ADDITIONAL_SYMPTOMS",
            result.InitialAnswers.Progression.NextQuestion!.Code);
        Assert.Equal(1, provider.CallCount);

        await using var db = CreateDbContext();
        var session = await db.PreTriageSessions.AsNoTracking()
            .Include(value => value.Answers)
            .SingleAsync(value => value.Id == EntityId.From(result.Session.SessionId));
        Assert.Equal(PreTriageSessionStatus.Active, session.Status);
        Assert.Equal(2, session.Answers.Count);
        Assert.Equal(2, (await CountsAsync()).Answers - before.Answers);
        Assert.Equal(1, (await CountsAsync()).Sessions - before.Sessions);
        Assert.Equal(before.History, (await CountsAsync()).History);
        Assert.Equal(before.FhirExports, (await CountsAsync()).FhirExports);

        using var followUpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            AnswersEndpoint(result.Session.SessionId))
        {
            Content = JsonContent.Create(new
            {
                structured = new { additionalSymptoms = Array.Empty<string>() }
            })
        };
        followUpRequest.Headers.Add(
            CapabilityHeader,
            result.Session.AnonymousCapability);
        using var followUp = await client.SendAsync(followUpRequest);
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
    }

    [Fact]
    public async Task ExactAlias_CreatesNormalSessionWithoutAi()
    {
        var provider = new ThrowingProvider(new InvalidOperationException(
            "Exact aliases must not invoke AI."));
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "Chest pain" });
        var result = await response.Content.ReadFromJsonAsync<OrchestrationResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("CHEST_PAIN", result!.Session!.Pathway);
        Assert.Empty(result.InitialAnswers!.AcceptedAnswers);
        Assert.Equal("DURATION", result.InitialAnswers.Progression.NextQuestion!.Code);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task PartialCandidateAcceptance_PersistsOnlyValuesValidForPinnedPackage()
    {
        var provider = new FixedProvider(Output(
            "HEADACHE",
            [
                Fact("DURATION", new ClinicalAiDurationValue(1, ClinicalDurationUnit.Days)),
                Fact("INTENSITY", new ClinicalAiIntegerValue(15))
            ]));
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "My head has hurt for one day" });
        var result = await response.Content.ReadFromJsonAsync<OrchestrationResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["DURATION"], result!.InitialAnswers!.AcceptedAnswers);
        Assert.Null(result.InitialAnswers.AcceptedValues.Intensity);
        Assert.Equal("INTENSITY", result.InitialAnswers.Progression.NextQuestion!.Code);
        await using var db = CreateDbContext();
        Assert.Equal(1, await db.TriageAnswers.CountAsync(value =>
            value.SessionId == EntityId.From(result.Session!.SessionId)));
    }

    [Theory]
    [InlineData(true, "AMBIGUOUS")]
    [InlineData(false, "UNRESOLVED")]
    public async Task NonResolvedOutcome_Returns200AndCreatesNoClinicalState(
        bool ambiguous,
        string expectedResolution)
    {
        var output = ambiguous
            ? Output(
                "HEADACHE",
                symptoms:
                [
                    Symptom("head hurts", "HEADACHE"),
                    Symptom("chest hurts", "CHEST_PAIN")
                ],
                ambiguities: [new ClinicalAiAmbiguity(ClinicalAiAmbiguityKind.Pathway)],
                intent: ClinicalIntentClassification.Ambiguous,
                requiresClarification: true)
            : Output(
                "OTHER_SYMPTOMS",
                ambiguities:
                [new ClinicalAiAmbiguity(ClinicalAiAmbiguityKind.InsufficientContext)],
                intent: ClinicalIntentClassification.Ambiguous,
                requiresClarification: true);
        using var factory = Factory(new FixedProvider(output));
        using var client = factory.CreateApiClient();
        var before = await CountsAsync();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { text = ambiguous ? "My chest and head hurt" : "I do not know" });
        var result = await response.Content.ReadFromJsonAsync<OrchestrationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedResolution, result!.Resolution);
        Assert.Null(result.Session);
        Assert.Null(result.InitialAnswers);
        Assert.Equal(before, await CountsAsync());
        if (ambiguous)
        {
            Assert.Equal(["HEADACHE", "CHEST_PAIN"], result.CandidatePathways);
        }
    }

    [Fact]
    public async Task ProviderFailure_ReturnsSafe503AndCreatesNoSession()
    {
        using var factory = Factory(new ThrowingProvider(
            new ClinicalAiProviderException(ClinicalAiProviderFailureCategory.Timeout)));
        using var client = factory.CreateApiClient();
        var before = await CountsAsync();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "My head has hurt all morning" });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("pre_triage.interpretation_unavailable",
            problem.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task RequestAndOptionalAuthenticationBoundariesAreEnforcedBeforeCreation()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var sessionsBefore = (await CountsAsync()).Sessions;

        using var blank = await client.PostAsJsonAsync(Endpoint, new { text = "   " });
        using var callerSelected = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "Headache", pathway = "CHEST_PAIN" });
        using var invalidBearerRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { text = "Headache" })
        };
        invalidBearerRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-valid-token");
        using var invalidBearer = await client.SendAsync(invalidBearerRequest);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, blank.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, callerSelected.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidBearer.StatusCode);
        Assert.Equal(sessionsBefore, (await CountsAsync()).Sessions);
    }

    [Fact]
    public async Task InitialPersistenceFailure_RollsBackSessionCreation()
    {
        using var logger = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: logger,
            configureServices: services =>
            {
                services.RemoveAll<IPreTriageAnswerRepository>();
                services.AddScoped<IPreTriageAnswerRepository, FailingAnswerRepository>();
            });
        using var client = factory.CreateApiClient();
        var sessionsBefore = (await CountsAsync()).Sessions;

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "Headache" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(sessionsBefore, (await CountsAsync()).Sessions);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("Pre-triage session", StringComparison.Ordinal) &&
                message.Contains("created in", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthenticatedReadyIntake_RemainsActiveThenCompletesIntoHistoryAndFhir()
    {
        var store = new InMemoryArtifactStore();
        var provider = new FixedProvider(Output(
            "HEADACHE",
            [
                Fact("DURATION", new ClinicalAiDurationValue(2, ClinicalDurationUnit.Days)),
                Fact("INTENSITY", new ClinicalAiIntegerValue(7)),
                Fact("ADDITIONAL_SYMPTOMS",
                    new ClinicalAiMultipleChoiceValue(["NAUSEA"]))
            ]));
        using var factory = Factory(provider, services =>
        {
            services.RemoveAll<IFhirArtifactStore>();
            services.AddSingleton<IFhirArtifactStore>(store);
        });
        using var client = factory.CreateApiClient();
        var identity = await CreateIdentityAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(identity.AccountId));

        using var intake = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "Headache for two days, 7 out of 10, with nausea" });
        var result = await intake.Content.ReadFromJsonAsync<OrchestrationResponse>();

        Assert.Equal(HttpStatusCode.Created, intake.StatusCode);
        Assert.Equal(identity.ProfileId.Value, result!.Session!.PatientId);
        Assert.Null(result.Session.AnonymousCapability);
        Assert.True(result.InitialAnswers!.Progression.ReadyToComplete);
        await using (var active = CreateDbContext())
        {
            var session = await active.PreTriageSessions.AsNoTracking().SingleAsync(
                value => value.Id == EntityId.From(result.Session.SessionId));
            Assert.Equal(PreTriageSessionStatus.Active, session.Status);
            Assert.Equal(0, await active.ClinicalHistoryEvents.CountAsync(value =>
                value.PatientProfileId == identity.ProfileId));
        }

        using var completion = await client.PostAsync(
            CompleteEndpoint(result.Session.SessionId),
            null);
        using var completed = JsonDocument.Parse(await completion.Content.ReadAsStringAsync());
        var episodeId = completed.RootElement.GetProperty("episodeId").GetGuid();
        Assert.Equal(HttpStatusCode.Created, completion.StatusCode);

        EntityId eventId;
        await using (var history = CreateDbContext())
        {
            var historyEvent = await history.ClinicalHistoryEvents.AsNoTracking().SingleAsync(
                value => value.SourceId == EntityId.From(episodeId));
            eventId = historyEvent.Id;
        }

        using var export = await client.PostAsJsonAsync(
            $"/api/v1/patients/{identity.ProfileId.Value:D}/fhir-exports",
            new
            {
                sourceClinicalHistoryEventId = eventId.Value,
                idempotencyKey = Guid.NewGuid()
            });

        Assert.Equal(HttpStatusCode.Created, export.StatusCode);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(1, store.Count);
        await using var verification = CreateDbContext();
        Assert.Equal(1, await verification.FhirExports.CountAsync(value =>
            value.PatientProfileId == identity.ProfileId));
    }

    private BeeexyApiFactory Factory(
        IClinicalAiProvider provider,
        Action<IServiceCollection>? configure = null) => new(
            postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<IClinicalAiProvider>();
                services.AddSingleton(provider);
                configure?.Invoke(services);
            });

    private async Task<TestIdentity> CreateIdentityAsync()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var account = Account.Create(
            NormalizedEmail.Create($"part3-{Guid.NewGuid():N}@example.com"),
            now);
        var profile = PatientProfile.Create(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            now,
            account.Id);
        var preference = UserPreference.Create(
            account.Id,
            UserTimeZone.Create("Etc/UTC"),
            now);
        await using var db = CreateDbContext();
        db.AddRange(account, profile, preference);
        await db.SaveChangesAsync();
        return new TestIdentity(account.Id, profile.Id);
    }

    private async Task<ClinicalCounts> CountsAsync()
    {
        await using var db = CreateDbContext();
        return new ClinicalCounts(
            await db.PreTriageSessions.CountAsync(),
            await db.TriageAnswers.CountAsync(),
            await db.PreTriageEpisodes.CountAsync(),
            await db.ClinicalHistoryEvents.CountAsync(),
            await db.FhirExports.CountAsync());
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private static string CreateJwt(EntityId accountId)
    {
        var now = DateTimeOffset.UtcNow;
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, accountId.Value.ToString("D")),
                new Claim("sid", Guid.NewGuid().ToString("D"))
            ],
            now.AddMinutes(-1).UtcDateTime,
            now.AddMinutes(10).UtcDateTime,
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

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

    private static string AnswersEndpoint(Guid id) =>
        $"/api/v1/pre-triage/sessions/{id:D}/answers";

    private static string CompleteEndpoint(Guid id) =>
        $"/api/v1/pre-triage/sessions/{id:D}/complete";

    private sealed class FixedProvider(ClinicalAiProviderOutput output) : IClinicalAiProvider
    {
        public int CallCount { get; private set; }

        public Task<ClinicalAiProviderOutput> InterpretAsync(
            ClinicalAiInterpretationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(output);
        }
    }

    private sealed class ThrowingProvider(Exception exception) : IClinicalAiProvider
    {
        public int CallCount { get; private set; }

        public Task<ClinicalAiProviderOutput> InterpretAsync(
            ClinicalAiInterpretationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException<ClinicalAiProviderOutput>(exception);
        }
    }

    private sealed class FailingAnswerRepository : IPreTriageAnswerRepository
    {
        public Task<PreTriageSession?> GetAsync(
            EntityId sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<PreTriageSession?>(
                new InvalidOperationException("Controlled initial-answer failure."));

        public Task<TResult?> MutateLockedAsync<TResult>(
            EntityId sessionId,
            Func<PreTriageSession, Task<TResult>> mutation,
            CancellationToken cancellationToken = default)
            where TResult : class => throw new NotSupportedException();
    }

    private sealed class InMemoryArtifactStore : IFhirArtifactStore
    {
        private readonly ConcurrentDictionary<string, byte[]> _artifacts =
            new(StringComparer.Ordinal);

        public int Count => _artifacts.Count;

        public Task StoreImmutableAsync(
            FhirArtifactStorageReference reference,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default)
        {
            if (!_artifacts.TryAdd(reference.PrivateUri, artifactBytes.ToArray()))
            {
                throw new FhirArtifactAlreadyExistsException();
            }

            return Task.CompletedTask;
        }

        public Task<byte[]> ReadAsync(
            FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_artifacts[reference.PrivateUri].ToArray());

        public Task<bool> DeleteAsync(
            FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_artifacts.TryRemove(reference.PrivateUri, out _));
    }

    private sealed record TestIdentity(EntityId AccountId, EntityId ProfileId);

    private sealed record ClinicalCounts(
        int Sessions,
        int Answers,
        int Episodes,
        int History,
        int FhirExports);

    private sealed class OrchestrationResponse
    {
        public string Resolution { get; init; } = null!;

        public IReadOnlyList<string>? CandidatePathways { get; init; }

        public SessionResponse? Session { get; init; }

        public InitialAnswerResponse? InitialAnswers { get; init; }
    }

    private sealed class SessionResponse
    {
        public Guid SessionId { get; init; }

        public Guid? PatientId { get; init; }

        public string Pathway { get; init; } = null!;

        public string Status { get; init; } = null!;

        public string? AnonymousCapability { get; init; }
    }

    private sealed class InitialAnswerResponse
    {
        public Guid SessionId { get; init; }

        public IReadOnlyList<string> AcceptedAnswers { get; init; } = [];

        public CandidateValuesResponse AcceptedValues { get; init; } = null!;

        public ProgressionResponse Progression { get; init; } = null!;
    }

    private sealed record CandidateValuesResponse(
        DurationResponse? Duration,
        int? Intensity,
        IReadOnlyList<string>? AdditionalSymptoms);

    private sealed record DurationResponse(decimal Value, string Unit);

    private sealed class ProgressionResponse
    {
        public NextQuestionResponse? NextQuestion { get; init; }

        public bool ReadyToComplete { get; init; }
    }

    private sealed record NextQuestionResponse(string Code);
}
