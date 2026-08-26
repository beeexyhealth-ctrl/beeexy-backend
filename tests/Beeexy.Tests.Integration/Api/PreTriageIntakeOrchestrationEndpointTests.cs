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
using Npgsql;

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
        AddIdempotencyKey(client);
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
        Assert.Equal("IN_PROGRESS", result.Conversation!.State);
        Assert.Equal(2, result.Conversation.Progress.Completed);
        Assert.Equal(3, result.Conversation.Progress.Total);
        Assert.Equal(67, result.Conversation.Progress.Percentage);
        Assert.Equal("additionalSymptoms", result.Conversation.NextInteraction!.Field);
        Assert.Equal("MULTI_SELECT", result.Conversation.NextInteraction.InputType);
        Assert.Equal(new DurationResponse(2, "DAYS"),
            result.Conversation.AcceptedValues.Duration);
        Assert.Equal(6, result.Conversation.AcceptedValues.Intensity);
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
        AddIdempotencyKey(client);

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "Chest pain" });
        var result = await response.Content.ReadFromJsonAsync<OrchestrationResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("CHEST_PAIN", result!.Session!.Pathway);
        Assert.Empty(result.InitialAnswers!.AcceptedAnswers);
        Assert.Equal("DURATION", result.InitialAnswers.Progression.NextQuestion!.Code);
        Assert.Equal("IN_PROGRESS", result.Conversation!.State);
        Assert.Equal(0, result.Conversation.Progress.Completed);
        Assert.Equal("duration", result.Conversation.NextInteraction!.Field);
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
        AddIdempotencyKey(client);

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "My head has hurt for one day" });
        var result = await response.Content.ReadFromJsonAsync<OrchestrationResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["DURATION"], result!.InitialAnswers!.AcceptedAnswers);
        Assert.Null(result.InitialAnswers.AcceptedValues.Intensity);
        Assert.Equal("INTENSITY", result.InitialAnswers.Progression.NextQuestion!.Code);
        Assert.Equal("IN_PROGRESS", result.Conversation!.State);
        Assert.Equal(1, result.Conversation.Progress.Completed);
        Assert.Equal(33, result.Conversation.Progress.Percentage);
        Assert.Equal("intensity", result.Conversation.NextInteraction!.Field);
        Assert.Equal("SCALE", result.Conversation.NextInteraction.InputType);
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
        AddIdempotencyKey(client);
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
        AddIdempotencyKey(client);
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
    public async Task SequentialAnonymousReplay_ReturnsCanonicalResultWithoutAiOrWrites()
    {
        var provider = new FixedProvider(Output(
            "HEADACHE",
            [Fact("DURATION", new ClinicalAiDurationValue(3, ClinicalDurationUnit.Hours))]));
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();
        var key = Guid.NewGuid().ToString("D");
        var before = await CountsAsync();

        using var first = await SendIntakeAsync(
            client,
            key,
            "My head has hurt for three hours");
        var created = await first.Content.ReadFromJsonAsync<OrchestrationResponse>();
        using var replay = await SendIntakeAsync(
            client,
            key,
            "My head has hurt for three hours",
            created!.Session!.AnonymousCapability);
        var repeated = await replay.Content.ReadFromJsonAsync<OrchestrationResponse>();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(created.Session.SessionId, repeated!.Session!.SessionId);
        Assert.Equal(created.Session.AnonymousCapability,
            repeated.Session.AnonymousCapability);
        Assert.Equal(created.InitialAnswers!.AcceptedAnswers,
            repeated.InitialAnswers!.AcceptedAnswers);
        Assert.Equal(1, provider.CallCount);
        var after = await CountsAsync();
        Assert.Equal(1, after.Sessions - before.Sessions);
        Assert.Equal(1, after.Answers - before.Answers);
        Assert.Equal(1, after.IdempotencyRecords - before.IdempotencyRecords);

        await using var freshContext = CreateDbContext();
        var mapping = await freshContext.PreTriageIntakeIdempotencyRecords
            .AsNoTracking()
            .SingleAsync(value => value.SessionId ==
                EntityId.From(created.Session.SessionId));
        Assert.StartsWith("sha256:", mapping.OperationKeyHash, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", mapping.RequestFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("head", mapping.RequestFingerprint,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SameScopedKeyWithDifferentText_ReturnsSafeConflictWithoutSecondOperation()
    {
        var provider = new FixedProvider(Output("HEADACHE"));
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();
        var key = Guid.NewGuid().ToString("D");
        var before = await CountsAsync();

        using var first = await SendIntakeAsync(client, key, "My head hurts");
        var created = await first.Content.ReadFromJsonAsync<OrchestrationResponse>();
        using var conflict = await SendIntakeAsync(
            client,
            key,
            "My stomach hurts",
            created!.Session!.AnonymousCapability);
        using var problem = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("pre_triage.idempotency_key_reused",
            problem.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("head", problem.RootElement.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(1, (await CountsAsync()).Sessions - before.Sessions);
    }

    [Fact]
    public async Task DifferentKeysWithSameText_CreateDistinctLegitimateOperations()
    {
        var provider = new FixedProvider(Output("HEADACHE"));
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();
        var before = await CountsAsync();

        using var first = await SendIntakeAsync(
            client,
            Guid.NewGuid().ToString("D"),
            "My head hurts");
        using var second = await SendIntakeAsync(
            client,
            Guid.NewGuid().ToString("D"),
            "My head hurts");
        var firstResult = await first.Content.ReadFromJsonAsync<OrchestrationResponse>();
        var secondResult = await second.Content.ReadFromJsonAsync<OrchestrationResponse>();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(firstResult!.Session!.SessionId, secondResult!.Session!.SessionId);
        Assert.Equal(2, provider.CallCount);
        Assert.Equal(2, (await CountsAsync()).Sessions - before.Sessions);
    }

    [Fact]
    public async Task ConcurrentAnonymousDuplicates_CommitOneOperationAndOneAiCall()
    {
        var provider = new DelayedProvider(Output("HEADACHE"));
        using var factory = Factory(provider);
        using var firstClient = factory.CreateApiClient();
        using var secondClient = factory.CreateApiClient();
        var key = Guid.NewGuid().ToString("D");
        var before = await CountsAsync();

        var firstTask = SendIntakeAsync(
            firstClient,
            key,
            "My head hurts");
        var secondTask = SendIntakeAsync(
            secondClient,
            key,
            "My head hurts");
        var responses = await Task.WhenAll(firstTask, secondTask);
        using var first = responses[0];
        using var second = responses[1];

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(value => value.StatusCode).Order().ToArray());
        Assert.Equal(1, provider.CallCount);
        var after = await CountsAsync();
        Assert.Equal(1, after.Sessions - before.Sessions);
        Assert.Equal(1, after.IdempotencyRecords - before.IdempotencyRecords);
    }

    [Fact]
    public async Task AnonymousScopes_IsolateTheSameKeyAcrossUnrelatedClients()
    {
        var provider = new FixedProvider(Output("HEADACHE"));
        using var factory = Factory(provider);
        using var firstClient = factory.CreateApiClient();
        using var unrelatedClient = factory.CreateApiClient();
        var key = Guid.NewGuid().ToString("D");

        using var first = await SendIntakeAsync(
            firstClient,
            key,
            "My head hurts",
            cookie: "pti1." + new string('A', 43));
        using var unrelated = await SendIntakeAsync(
            unrelatedClient,
            key,
            "My head hurts",
            cookie: "pti1." + new string('B', 43));
        var firstResult = await first.Content.ReadFromJsonAsync<OrchestrationResponse>();
        var unrelatedResult = await unrelated.Content.ReadFromJsonAsync<OrchestrationResponse>();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, unrelated.StatusCode);
        Assert.NotEqual(firstResult!.Session!.SessionId,
            unrelatedResult!.Session!.SessionId);
        Assert.NotEqual(firstResult.Session.AnonymousCapability,
            unrelatedResult.Session.AnonymousCapability);
    }

    [Fact]
    public async Task ProviderFailure_DoesNotPoisonKeyForLaterHealthyRetry()
    {
        var provider = new FailOnceProvider(Output("HEADACHE"));
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();
        var key = Guid.NewGuid().ToString("D");
        var before = await CountsAsync();

        using var unavailable = await SendIntakeAsync(
            client,
            key,
            "My head hurts this morning");
        using var recovered = await SendIntakeAsync(
            client,
            key,
            "My head hurts this morning");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        Assert.Equal(2, provider.CallCount);
        Assert.Equal(1, (await CountsAsync()).Sessions - before.Sessions);
    }

    [Fact]
    public async Task AuthenticatedScope_ReplaysPerAccountAndDoesNotCrossAccounts()
    {
        var provider = new FixedProvider(Output("HEADACHE"));
        using var factory = Factory(provider);
        using var firstClient = factory.CreateApiClient();
        using var unrelatedClient = factory.CreateApiClient();
        var firstIdentity = await CreateIdentityAsync();
        var unrelatedIdentity = await CreateIdentityAsync();
        firstClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(firstIdentity.AccountId));
        unrelatedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(unrelatedIdentity.AccountId));
        var key = Guid.NewGuid().ToString("D");

        using var first = await SendIntakeAsync(firstClient, key, "My head hurts");
        using var replay = await SendIntakeAsync(firstClient, key, "My head hurts");
        using var unrelated = await SendIntakeAsync(
            unrelatedClient,
            key,
            "My head hurts");
        var firstResult = await first.Content.ReadFromJsonAsync<OrchestrationResponse>();
        var replayResult = await replay.Content.ReadFromJsonAsync<OrchestrationResponse>();
        var unrelatedResult = await unrelated.Content.ReadFromJsonAsync<OrchestrationResponse>();

        Assert.Equal(firstResult!.Session!.SessionId, replayResult!.Session!.SessionId);
        Assert.NotEqual(firstResult.Session.SessionId,
            unrelatedResult!.Session!.SessionId);
        Assert.Equal(firstIdentity.ProfileId.Value, firstResult.Session.PatientId);
        Assert.Equal(unrelatedIdentity.ProfileId.Value, unrelatedResult.Session.PatientId);
        Assert.Equal(2, provider.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("contains whitespace")]
    public async Task InvalidOrMissingIdempotencyKey_IsRejectedWithoutProviderCall(string? key)
    {
        var provider = new FixedProvider(Output("HEADACHE"));
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();

        using var response = await SendIntakeAsync(client, key, "My head hurts");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("pre_triage.idempotency_key_invalid",
            problem.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task OversizedIdempotencyKey_IsRejected()
    {
        using var factory = Factory(new FixedProvider(Output("HEADACHE")));
        using var client = factory.CreateApiClient();

        using var response = await SendIntakeAsync(
            client,
            new string('a', 129),
            "My head hurts");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RequestAndOptionalAuthenticationBoundariesAreEnforcedBeforeCreation()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        AddIdempotencyKey(client);
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
        AddIdempotencyKey(client);
        var sessionsBefore = (await CountsAsync()).Sessions;
        var mappingsBefore = (await CountsAsync()).IdempotencyRecords;

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { text = "Headache" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(sessionsBefore, (await CountsAsync()).Sessions);
        Assert.Equal(mappingsBefore, (await CountsAsync()).IdempotencyRecords);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("Pre-triage session", StringComparison.Ordinal) &&
                message.Contains("created in", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IdempotencyCompletionFailure_RollsBackSessionAnswersAndMapping()
    {
        using var factory = Factory(new FixedProvider(Output("HEADACHE")));
        using var client = factory.CreateApiClient();
        AddIdempotencyKey(client);
        var before = await CountsAsync();
        await ExecuteSqlAsync(
            """
            CREATE OR REPLACE FUNCTION triage.reject_intake_idempotency_for_test()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                RAISE EXCEPTION 'controlled intake idempotency failure';
            END;
            $$;
            CREATE TRIGGER reject_intake_idempotency_for_test
            BEFORE INSERT ON triage.pre_triage_intake_idempotency
            FOR EACH ROW
            EXECUTE FUNCTION triage.reject_intake_idempotency_for_test();
            """);

        try
        {
            using var response = await client.PostAsJsonAsync(
                Endpoint,
                new { text = "Headache" });

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(before, await CountsAsync());
        }
        finally
        {
            await ExecuteSqlAsync(
                """
                DROP TRIGGER IF EXISTS reject_intake_idempotency_for_test
                    ON triage.pre_triage_intake_idempotency;
                DROP FUNCTION IF EXISTS triage.reject_intake_idempotency_for_test();
                """);
        }
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
        AddIdempotencyKey(client);
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
            await db.FhirExports.CountAsync(),
            await db.PreTriageIntakeIdempotencyRecords.CountAsync());
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

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

    private static void AddIdempotencyKey(HttpClient client) =>
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));

    private static async Task<HttpResponseMessage> SendIntakeAsync(
        HttpClient client,
        string? idempotencyKey,
        string text,
        string? anonymousCapability = null,
        string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { text })
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        if (anonymousCapability is not null)
        {
            request.Headers.Add(CapabilityHeader, anonymousCapability);
        }

        if (cookie is not null)
        {
            request.Headers.Add(
                "Cookie",
                $"Beeexy.PreTriage.IntakeScope={cookie}");
        }

        return await client.SendAsync(request);
    }

    private sealed class FixedProvider(ClinicalAiProviderOutput output) : IClinicalAiProvider
    {
        private int callCount;

        public int CallCount => callCount;

        public Task<ClinicalAiProviderOutput> InterpretAsync(
            ClinicalAiInterpretationRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(output);
        }
    }

    private sealed class DelayedProvider(ClinicalAiProviderOutput output) : IClinicalAiProvider
    {
        private int callCount;

        public int CallCount => callCount;

        public async Task<ClinicalAiProviderOutput> InterpretAsync(
            ClinicalAiInterpretationRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            await Task.Delay(100, cancellationToken);
            return output;
        }
    }

    private sealed class FailOnceProvider(ClinicalAiProviderOutput output) : IClinicalAiProvider
    {
        private int callCount;

        public int CallCount => callCount;

        public Task<ClinicalAiProviderOutput> InterpretAsync(
            ClinicalAiInterpretationRequest request,
            CancellationToken cancellationToken = default)
        {
            var invocation = Interlocked.Increment(ref callCount);
            return invocation == 1
                ? Task.FromException<ClinicalAiProviderOutput>(
                    new ClinicalAiProviderException(
                        ClinicalAiProviderFailureCategory.Timeout))
                : Task.FromResult(output);
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
        int FhirExports,
        int IdempotencyRecords);

    private sealed class OrchestrationResponse
    {
        public string Resolution { get; init; } = null!;

        public IReadOnlyList<string>? CandidatePathways { get; init; }

        public SessionResponse? Session { get; init; }

        public InitialAnswerResponse? InitialAnswers { get; init; }

        public ConversationResponse? Conversation { get; init; }
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

    private sealed class ConversationResponse
    {
        public string State { get; init; } = null!;

        public ConversationProgressResponse Progress { get; init; } = null!;

        public CandidateValuesResponse AcceptedValues { get; init; } = null!;

        public ConversationInteractionResponse? NextInteraction { get; init; }
    }

    private sealed record ConversationProgressResponse(
        int Completed,
        int Total,
        int Percentage);

    private sealed record ConversationInteractionResponse(
        string Field,
        string InputType);

    private sealed record NextQuestionResponse(string Code);
}
