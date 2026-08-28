using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Beeexy.Application.Common;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class PreTriageConversationEndpointTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private const string StartEndpoint = "/api/v1/pre-triage/sessions";
    private const string CapabilityHeader = "X-Pre-Triage-Capability";
    private const string SigningKey =
        "integration-test-only-jwt-signing-key-with-at-least-32-bytes";
    private const string Issuer = "https://api.beeexy.com";
    private const string Audience = "beeexy-client";
    private const string VersionTestSource = "part-4-conversation-version-test";
    private EntityId[] _preexistingSessionIds = [];

    [Theory]
    [InlineData("HEADACHE", "Headache")]
    [InlineData("ABDOMINAL_PAIN", "Stomach pain")]
    [InlineData("CHEST_PAIN", "Chest pain")]
    [InlineData("FEVER", "Fever")]
    [InlineData("OTHER_SYMPTOMS", "Other symptoms")]
    public async Task AllPathways_StartWithCanonicalEducationalOrDurationProjection(
        string pathway,
        string label)
    {
        var provider = new FailIfInvokedClinicalAiProvider();
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();
        var session = await StartAnonymousAsync(client, pathway);

        Assert.NotNull(session.Conversation);
        Assert.Equal(session.SessionId, session.Conversation.SessionId);
        Assert.Equal("ACTIVE", session.Conversation.SessionStatus);
        Assert.Equal("IN_PROGRESS", session.Conversation.State);
        Assert.Equal(new PathwayResponse(pathway, label), session.Conversation.Pathway);
        Assert.Equal(new ProgressResponse(0, 3, 0), session.Conversation.Progress);
        if (pathway == "OTHER_SYMPTOMS")
        {
            Assert.Equal("QUESTION", session.Conversation.NextInteraction!.Type);
            Assert.Equal("duration", session.Conversation.NextInteraction.Field);
            Assert.Equal("DURATION", session.Conversation.NextInteraction.QuestionCode);
            Assert.Equal("DURATION", session.Conversation.NextInteraction.InputType);
            Assert.True(session.Conversation.NextInteraction.Required);
            Assert.Null(session.Conversation.NextInteraction.Video);
        }
        else
        {
            Assert.Equal("EDUCATIONAL_VIDEO_OFFER",
                session.Conversation.NextInteraction!.Type);
            Assert.Equal("educationalVideoDecision",
                session.Conversation.NextInteraction.Field);
            Assert.Null(session.Conversation.NextInteraction.QuestionCode);
            Assert.Equal("SINGLE_SELECT", session.Conversation.NextInteraction.InputType);
            Assert.False(session.Conversation.NextInteraction.Required);
            Assert.Equal(
                [
                    new OptionResponse("WATCH", "Yes, show me the video"),
                    new OptionResponse("SKIP", "No, continue with assessment")
                ],
                session.Conversation.NextInteraction.Options);
            Assert.NotNull(session.Conversation.NextInteraction.Video);
            Assert.StartsWith("https://res.cloudinary.com/",
                session.Conversation.NextInteraction.Video.Url);
        }

        using var refresh = await GetConversationAsync(client, session);
        var refreshed = await refresh.Content.ReadFromJsonAsync<ConversationResponse>();
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(session.Conversation.SessionId, refreshed!.SessionId);
        Assert.Equal(session.Conversation.State, refreshed.State);
        Assert.Equal(session.Conversation.Pathway, refreshed.Pathway);
        Assert.Equal(session.Conversation.Progress, refreshed.Progress);
        Assert.Equal(session.Conversation.NextInteraction.Field,
            refreshed.NextInteraction!.Field);
        Assert.Equal(session.Conversation.NextInteraction.Prompt,
            refreshed.NextInteraction.Prompt);
        Assert.Equal(0, provider.CallCount);
    }

    [Theory]
    [InlineData("WATCH")]
    [InlineData("SKIP")]
    public async Task WatchAndSkip_ArePersistedIdempotentlyAndAdvanceWithoutClinicalValues(
        string decision)
    {
        using var factory = Factory(new FailIfInvokedClinicalAiProvider());
        using var client = factory.CreateApiClient();
        var session = await StartAnonymousAsync(client, "CHEST_PAIN");

        var first = await ResolveOfferAsync(client, session, decision);
        var repeated = await ResolveOfferAsync(client, session, decision);
        using var refresh = await GetConversationAsync(client, session);
        var refreshed = await refresh.Content.ReadFromJsonAsync<ConversationResponse>();

        Assert.Equal(decision, first.Decision);
        Assert.True(first.NewlyResolved);
        Assert.False(repeated.NewlyResolved);
        Assert.Equal(first.ResolvedAt, repeated.ResolvedAt);
        AssertClinicalValuesEmpty(first.Conversation.AcceptedValues);
        Assert.Equal("QUESTION", first.Conversation.NextInteraction!.Type);
        Assert.Equal("DURATION", first.Conversation.NextInteraction.QuestionCode);
        Assert.Equal(first.Conversation.NextInteraction.Field,
            repeated.Conversation.NextInteraction!.Field);
        Assert.Equal(first.Conversation.NextInteraction.QuestionCode,
            repeated.Conversation.NextInteraction.QuestionCode);
        Assert.Equal(first.Conversation.NextInteraction.Field,
            refreshed!.NextInteraction!.Field);
        Assert.Equal(first.Conversation.NextInteraction.QuestionCode,
            refreshed.NextInteraction.QuestionCode);

        await using var db = CreateDbContext();
        var stored = await db.PreTriageSessions.AsNoTracking().SingleAsync(value =>
            value.Id == EntityId.From(session.SessionId));
        Assert.Equal(decision.ToLowerInvariant(),
            stored.EducationalVideoDecision!.Value.ToString().ToLowerInvariant());
        Assert.Empty(stored.Answers);
    }

    [Theory]
    [InlineData("PLAY")]
    [InlineData("watch")]
    public async Task InvalidEducationalDecision_IsRejectedWithoutResolvingOffer(string decision)
    {
        using var factory = Factory(new FailIfInvokedClinicalAiProvider());
        using var client = factory.CreateApiClient();
        var session = await StartAnonymousAsync(client, "FEVER");

        using var response = await SendOfferDecisionAsync(client, session, decision);
        using var refresh = await GetConversationAsync(client, session);
        var projection = await refresh.Content.ReadFromJsonAsync<ConversationResponse>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("EDUCATIONAL_VIDEO_OFFER", projection!.NextInteraction!.Type);
        AssertClinicalValuesEmpty(projection.AcceptedValues);
    }

    [Fact]
    public async Task AcceptedAnswers_AdvanceEmbeddedAndRefreshedProjectionToReview()
    {
        using var factory = Factory(new FailIfInvokedClinicalAiProvider());
        using var client = factory.CreateApiClient();
        var session = await StartAnonymousAsync(client, "ABDOMINAL_PAIN");
        _ = await ResolveOfferAsync(client, session, "SKIP");
        var sideEffectsBefore = await PermanentCountsAsync();

        var afterDuration = await SubmitAsync(client, session, new
        {
            structured = new { duration = new { value = 2, unit = "DAYS" } }
        });
        Assert.Equal(new ProgressResponse(1, 3, 33), afterDuration.Progress);
        Assert.Equal(new DurationResponse(2, "DAYS"), afterDuration.AcceptedValues.Duration);
        Assert.Equal("intensity", afterDuration.NextInteraction!.Field);
        Assert.Equal("SCALE", afterDuration.NextInteraction.InputType);
        Assert.Equal(1, afterDuration.NextInteraction.Constraints.Minimum);
        Assert.Equal(10, afterDuration.NextInteraction.Constraints.Maximum);
        Assert.Equal(1, afterDuration.NextInteraction.Constraints.Step);

        var afterIntensity = await SubmitAsync(client, session, new
        {
            structured = new { intensity = 6 }
        });
        Assert.Equal(new ProgressResponse(2, 3, 67), afterIntensity.Progress);
        Assert.Equal(6, afterIntensity.AcceptedValues.Intensity);
        Assert.Equal("additionalSymptoms", afterIntensity.NextInteraction!.Field);
        Assert.Equal("MULTI_SELECT", afterIntensity.NextInteraction.InputType);
        Assert.Equal(
            [
                new OptionResponse("NAUSEA", "Nausea"),
                new OptionResponse("DIARRHEA", "Diarrhea"),
                new OptionResponse("FEVER", "Fever")
            ],
            afterIntensity.NextInteraction.Options);
        Assert.True(afterIntensity.NextInteraction.Constraints.AllowsEmptySelection);

        var ready = await SubmitAsync(client, session, new
        {
            structured = new { additionalSymptoms = Array.Empty<string>() }
        });
        Assert.Equal("READY_FOR_REVIEW", ready.State);
        Assert.Equal("ACTIVE", ready.SessionStatus);
        Assert.Equal(new ProgressResponse(3, 3, 100), ready.Progress);
        Assert.Null(ready.NextInteraction);
        Assert.Empty(ready.AcceptedValues.AdditionalSymptoms!);

        using var refresh = await GetConversationAsync(client, session);
        var refreshed = await refresh.Content.ReadFromJsonAsync<ConversationResponse>();
        Assert.Equal(ready.State, refreshed!.State);
        Assert.Equal(ready.Progress, refreshed.Progress);
        Assert.Equal(ready.AcceptedValues.Duration, refreshed.AcceptedValues.Duration);
        Assert.Equal(ready.AcceptedValues.Intensity, refreshed.AcceptedValues.Intensity);
        Assert.Equal(ready.AcceptedValues.AdditionalSymptoms,
            refreshed.AcceptedValues.AdditionalSymptoms);
        Assert.Null(refreshed.NextInteraction);
        Assert.Equal(sideEffectsBefore, await PermanentCountsAsync());
        await using var db = CreateDbContext();
        Assert.Equal(
            PreTriageSessionStatus.Active,
            (await db.PreTriageSessions.AsNoTracking().SingleAsync(value =>
                value.Id == EntityId.From(session.SessionId))).Status);
    }

    [Fact]
    public async Task ExplicitCompletion_ProjectsCompletedWithoutReopening()
    {
        using var factory = Factory(new FailIfInvokedClinicalAiProvider());
        using var client = factory.CreateApiClient();
        var session = await StartAnonymousAsync(client, "HEADACHE");
        _ = await ResolveOfferAsync(client, session, "SKIP");
        _ = await SubmitAsync(client, session, new
        {
            structured = new
            {
                duration = new { value = 1, unit = "DAYS" },
                intensity = 5,
                additionalSymptoms = new[] { "NAUSEA" }
            }
        });
        using var completion = await SendWithCapabilityAsync(
            client,
            HttpMethod.Post,
            CompleteEndpoint(session.SessionId),
            session.AnonymousCapability);
        Assert.Equal(HttpStatusCode.Created, completion.StatusCode);

        using var response = await GetConversationAsync(client, session);
        var projection = await response.Content.ReadFromJsonAsync<ConversationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("COMPLETED", projection!.State);
        Assert.Equal("COMPLETED", projection.SessionStatus);
        Assert.Equal(new ProgressResponse(3, 3, 100), projection.Progress);
        Assert.Null(projection.NextInteraction);
        Assert.Equal(new DurationResponse(1, "DAYS"), projection.AcceptedValues.Duration);
        Assert.Equal(5, projection.AcceptedValues.Intensity);
        Assert.Equal(["NAUSEA"], projection.AcceptedValues.AdditionalSymptoms);

        using var answerAttempt = await SubmitWithCapabilityAsync(
            client,
            session,
            new { structured = new { intensity = 7 } });
        Assert.Equal(HttpStatusCode.Conflict, answerAttempt.StatusCode);
    }

    [Fact]
    public async Task AnonymousCapability_IsRequiredAndCannotCrossSessions()
    {
        using var factory = Factory(new FailIfInvokedClinicalAiProvider());
        using var client = factory.CreateApiClient();
        var first = await StartAnonymousAsync(client, "HEADACHE");
        var second = await StartAnonymousAsync(client, "FEVER");

        using var missing = await client.GetAsync(ConversationEndpoint(first.SessionId));
        using var wrong = await GetConversationAsync(
            client,
            first.SessionId,
            "wrong-capability");
        using var cross = await GetConversationAsync(
            client,
            first.SessionId,
            second.AnonymousCapability);
        using var authorized = await GetConversationAsync(client, first);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cross.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedOwnerSucceedsAndAnotherAccountReceivesConcealedNotFound()
    {
        var owner = await CreateIdentityAsync("owner");
        var other = await CreateIdentityAsync("other");
        using var factory = Factory(new FailIfInvokedClinicalAiProvider());
        using var ownerClient = factory.CreateApiClient();
        using var otherClient = factory.CreateApiClient();
        SetBearer(ownerClient, owner.Token);
        SetBearer(otherClient, other.Token);
        using var start = await ownerClient.PostAsJsonAsync(
            StartEndpoint,
            new { pathway = "CHEST_PAIN" });
        var session = await start.Content.ReadFromJsonAsync<StartResponse>();

        using var ownerRead = await ownerClient.GetAsync(
            ConversationEndpoint(session!.SessionId));
        using var otherRead = await otherClient.GetAsync(
            ConversationEndpoint(session.SessionId));

        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherRead.StatusCode);
    }

    [Fact]
    public async Task ExpiredActiveSession_PreservesConcealedNotFoundBehavior()
    {
        using var setupFactory = Factory(new FailIfInvokedClinicalAiProvider());
        using var setupClient = setupFactory.CreateApiClient();
        var session = await StartAnonymousAsync(setupClient, "OTHER_SYMPTOMS");
        using var expiredFactory = Factory(
            new FailIfInvokedClinicalAiProvider(),
            services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(
                    new FixedClock(DateTimeOffset.UtcNow.AddDays(2)));
            });
        using var expiredClient = expiredFactory.CreateApiClient();

        using var response = await GetConversationAsync(expiredClient, session);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OldSessionProjection_RemainsPinnedAfterNewVersionActivation()
    {
        var provider = new FailIfInvokedClinicalAiProvider();
        var versionOne = await ImportVersionAsync(
            "part4-v1",
            "How many days has the pinned v1 pain lasted?",
            1);
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();
        var session = await StartAnonymousAsync(client, "ABDOMINAL_PAIN");
        Assert.Equal(versionOne.Version.Value, session.Conversation!.Questionnaire.Version);
        await ImportVersionAsync(
            "part4-v2",
            "This v2 prompt must never appear for the old session.",
            2);
        _ = await ResolveOfferAsync(client, session, "SKIP");

        using var response = await GetConversationAsync(client, session);
        var projection = await response.Content.ReadFromJsonAsync<ConversationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("part4-v1", projection!.Questionnaire.Version);
        Assert.Equal("part4-v1", projection.RuleSet.Version);
        Assert.Equal("How many days has the pinned v1 pain lasted?",
            projection.NextInteraction!.Prompt);
        Assert.Equal("DURATION", projection.NextInteraction.InputType);
        Assert.Equal(
            ["MINUTES", "HOURS", "DAYS", "WEEKS", "MONTHS"],
            projection.NextInteraction.Constraints.AllowedUnits);
        Assert.Equal(new ProgressResponse(0, 3, 0), projection.Progress);

        var afterDuration = await SubmitAsync(client, session, new
        {
            questionnaireVersion = "part4-v1",
            structured = new { duration = new { value = 2, unit = "DAYS" } }
        });
        Assert.Equal("intensity", afterDuration.NextInteraction!.Field);
        Assert.Equal(10, afterDuration.NextInteraction.Constraints.Maximum);

        var acceptedUnderPinnedV1 = await SubmitAsync(client, session, new
        {
            questionnaireVersion = "part4-v1",
            structured = new { intensity = 6 }
        });
        Assert.Equal(6, acceptedUnderPinnedV1.AcceptedValues.Intensity);
        Assert.Equal("additionalSymptoms",
            acceptedUnderPinnedV1.NextInteraction!.Field);
        Assert.Equal("part4-v1", acceptedUnderPinnedV1.Questionnaire.Version);

        using var v2VersionAttempt = await SubmitWithCapabilityAsync(
            client,
            session,
            new
            {
                questionnaireVersion = "part4-v2",
                structured = new { additionalSymptoms = Array.Empty<string>() }
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, v2VersionAttempt.StatusCode);
        Assert.Equal(0, provider.CallCount);
    }

    public async Task InitializeAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        _preexistingSessionIds = await db.PreTriageSessions.AsNoTracking()
            .Select(value => value.Id)
            .ToArrayAsync();
        var importer = Importer(db);
        foreach (var package in SimplifiedDemoDefinitionPackages.CreateAll())
        {
            await importer.ImportAsync(package);
        }
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateDbContext();
        var sessionIds = await db.PreTriageSessions
            .Where(value => !_preexistingSessionIds.Contains(value.Id))
            .Select(value => value.Id)
            .ToArrayAsync();
        var episodeIds = await db.PreTriageEpisodes
            .Where(value => sessionIds.Contains(value.SourceSessionId))
            .Select(value => value.Id)
            .ToArrayAsync();
        await db.ClinicalFindings
            .Where(value => db.ClinicalAssessments
                .Where(assessment => episodeIds.Contains(assessment.EpisodeId))
                .Select(assessment => assessment.Id)
                .Contains(value.AssessmentId))
            .ExecuteDeleteAsync();
        await db.ClinicalAssessments
            .Where(value => episodeIds.Contains(value.EpisodeId))
            .ExecuteDeleteAsync();
        var answers = (await db.TriageAnswers
                .Where(value => value.EpisodeId != null)
                .ToArrayAsync())
            .Where(value => episodeIds.Contains(value.EpisodeId!.Value))
            .ToArray();
        var symptoms = (await db.ReportedSymptoms
                .Where(value => value.EpisodeId != null)
                .ToArrayAsync())
            .Where(value => episodeIds.Contains(value.EpisodeId!.Value))
            .ToArray();
        db.RemoveRange(answers);
        db.RemoveRange(symptoms);
        await db.ClinicalHistoryEvents
            .Where(value => episodeIds.Contains(value.SourceId))
            .ExecuteDeleteAsync();
        await db.PreTriageHistoryProjectionRecords
            .Where(value => episodeIds.Contains(value.SourceEpisodeId))
            .ExecuteDeleteAsync();
        await db.SaveChangesAsync();
        await db.PreTriageEpisodes
            .Where(value => episodeIds.Contains(value.Id))
            .ExecuteDeleteAsync();
        await db.PreTriageSessions
            .Where(value => sessionIds.Contains(value.Id))
            .ExecuteDeleteAsync();

        var questionnaireIds = await db.QuestionnaireVersions
            .Where(value => value.SourceReference == VersionTestSource)
            .Select(value => value.Id)
            .ToArrayAsync();
        await db.TriageQuestions
            .Where(value => questionnaireIds.Contains(value.QuestionnaireVersionId))
            .ExecuteDeleteAsync();
        await db.QuestionnaireVersions
            .Where(value => value.SourceReference == VersionTestSource)
            .ExecuteDeleteAsync();
        await db.ClinicalRuleSetVersions
            .Where(value => value.SourceReference == VersionTestSource)
            .ExecuteDeleteAsync();
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

    private async Task<StartResponse> StartAnonymousAsync(
        HttpClient client,
        string pathway)
    {
        using var response = await client.PostAsJsonAsync(StartEndpoint, new { pathway });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StartResponse>())!;
    }

    private static async Task<ConversationResponse> SubmitAsync(
        HttpClient client,
        StartResponse session,
        object body)
    {
        using var response = await SubmitWithCapabilityAsync(client, session, body);
        var result = await response.Content.ReadFromJsonAsync<AnswerResponse>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result!.Conversation);
        return result.Conversation;
    }

    private static Task<HttpResponseMessage> SubmitWithCapabilityAsync(
        HttpClient client,
        StartResponse session,
        object body)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            AnswerEndpoint(session.SessionId))
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(CapabilityHeader, session.AnonymousCapability);
        return client.SendAsync(request);
    }

    private static async Task<OfferDecisionResponse> ResolveOfferAsync(
        HttpClient client,
        StartResponse session,
        string decision)
    {
        using var response = await SendOfferDecisionAsync(client, session, decision);
        var result = await response.Content.ReadFromJsonAsync<OfferDecisionResponse>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return result!;
    }

    private static Task<HttpResponseMessage> SendOfferDecisionAsync(
        HttpClient client,
        StartResponse session,
        string decision)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            EducationalVideoOfferEndpoint(session.SessionId))
        {
            Content = JsonContent.Create(new { decision })
        };
        request.Headers.Add(CapabilityHeader, session.AnonymousCapability);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> GetConversationAsync(
        HttpClient client,
        StartResponse session) => GetConversationAsync(
            client,
            session.SessionId,
            session.AnonymousCapability);

    private static Task<HttpResponseMessage> GetConversationAsync(
        HttpClient client,
        Guid sessionId,
        string capability) => SendWithCapabilityAsync(
            client,
            HttpMethod.Get,
            ConversationEndpoint(sessionId),
            capability);

    private static Task<HttpResponseMessage> SendWithCapabilityAsync(
        HttpClient client,
        HttpMethod method,
        string endpoint,
        string capability)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Add(CapabilityHeader, capability);
        return client.SendAsync(request);
    }

    private async Task<ClinicalDefinitionPackage> ImportVersionAsync(
        string versionValue,
        string durationPrompt,
        int activationDayOffset)
    {
        var original = SimplifiedDemoDefinitionPackages.Create(
            ClinicalPathways.AbdominalPain);
        var version = DefinitionVersion.Create(versionValue);
        var importedAt = original.Questionnaire.ImportedAt.AddDays(activationDayOffset);
        var questions = original.Questions.Select(question =>
            question.Code.Value == "DURATION"
                ? question with { PromptText = durationPrompt }
                : question).ToArray();
        var questionInputs = questions.Select(question => new TriageQuestionInput(
            question.Code,
            question.PromptText,
            question.DisplayOrder,
            ClinicalDefinitionSerialization.SerializeQuestion(question))).ToArray();
        var questionnaire = QuestionnaireDefinitionVersion.Import(
            original.Pathway,
            original.Questionnaire.QuestionnaireCode,
            version,
            ClinicalDefinitionIntegrity.QuestionnaireHash(questionInputs),
            original.ContentStatus,
            importedAt,
            activatedAt: importedAt,
            sourceReference: VersionTestSource,
            questions: questionInputs);
        var ruleSet = ClinicalRuleSetVersion.Import(
            original.Pathway,
            original.RuleSet.RuleSetCode,
            version,
            original.RuleSet.ContentHash,
            original.ContentStatus,
            original.RuleSet.DefinitionMetadataJson,
            importedAt,
            activatedAt: importedAt,
            sourceReference: VersionTestSource);
        var package = new ClinicalDefinitionPackage(
            original.Pathway,
            questionnaire,
            ruleSet,
            questions,
            original.Branches,
            original.RuleDefinitions);
        await using var db = CreateDbContext();
        await Importer(db).ImportAsync(package);
        return package;
    }

    private async Task<TestIdentity> CreateIdentityAsync(string suffix)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var account = Account.Create(
            NormalizedEmail.Create($"part4-{suffix}-{Guid.NewGuid():N}@example.com"),
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
        return new TestIdentity(account.Id, CreateJwt(account.Id));
    }

    private static string CreateJwt(EntityId accountId)
    {
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, accountId.Value.ToString("D")),
                new Claim("sid", Guid.NewGuid().ToString("D"))
            ],
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

    private async Task<(int Episodes, int History, int FhirExports)> PermanentCountsAsync()
    {
        await using var db = CreateDbContext();
        return (
            await db.PreTriageEpisodes.CountAsync(),
            await db.ClinicalHistoryEvents.CountAsync(),
            await db.FhirExports.CountAsync());
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private static ClinicalDefinitionImporter Importer(BeeexyDbContext db) => new(
        db,
        new ClinicalDefinitionPackageValidator(),
        NullLogger<ClinicalDefinitionImporter>.Instance);

    private static string ConversationEndpoint(Guid sessionId) =>
        $"/api/v1/pre-triage/sessions/{sessionId:D}/conversation";

    private static string AnswerEndpoint(Guid sessionId) =>
        $"/api/v1/pre-triage/sessions/{sessionId:D}/answers";

    private static string EducationalVideoOfferEndpoint(Guid sessionId) =>
        $"/api/v1/pre-triage/sessions/{sessionId:D}/educational-video-offer";

    private static string CompleteEndpoint(Guid sessionId) =>
        $"/api/v1/pre-triage/sessions/{sessionId:D}/complete";

    private sealed class FailIfInvokedClinicalAiProvider : IClinicalAiProvider
    {
        public int CallCount { get; private set; }

        public Task<ClinicalAiProviderOutput> InterpretAsync(
            ClinicalAiInterpretationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException(
                "Conversation projection must never invoke the clinical AI provider.");
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed record TestIdentity(EntityId AccountId, string Token);

    private sealed record StartResponse(
        Guid SessionId,
        string Pathway,
        string Status,
        string AnonymousCapability,
        ConversationResponse? Conversation);

    private sealed record AnswerResponse(ConversationResponse Conversation);

    private sealed record OfferDecisionResponse(
        Guid SessionId,
        string Decision,
        DateTimeOffset ResolvedAt,
        bool NewlyResolved,
        ConversationResponse Conversation);

    private sealed record ConversationResponse(
        Guid SessionId,
        string SessionStatus,
        string State,
        DateTimeOffset ExpiresAt,
        PathwayResponse Pathway,
        DefinitionResponse Questionnaire,
        DefinitionResponse RuleSet,
        ProgressResponse Progress,
        AcceptedValuesResponse AcceptedValues,
        InteractionResponse? NextInteraction);

    private sealed record PathwayResponse(string Code, string Label);

    private sealed record DefinitionResponse(string Code, string Version);

    private sealed record ProgressResponse(int Completed, int Total, int Percentage);

    private sealed record AcceptedValuesResponse(
        DurationResponse? Duration,
        int? Intensity,
        IReadOnlyList<string>? AdditionalSymptoms);

    private sealed record DurationResponse(decimal Value, string Unit);

    private sealed record InteractionResponse(
        string Type,
        string Field,
        string? QuestionCode,
        string Prompt,
        string InputType,
        bool Required,
        ConstraintsResponse Constraints,
        IReadOnlyList<OptionResponse> Options,
        VideoResponse? Video);

    private sealed record VideoResponse(string Id, string Title, string Url);

    private sealed record ConstraintsResponse(
        decimal? Minimum,
        decimal? Maximum,
        decimal? Step,
        bool? ExclusiveMinimum,
        IReadOnlyList<string>? AllowedUnits,
        int? MinimumSelections,
        int? MaximumSelections,
        bool? AllowsEmptySelection);

    private sealed record OptionResponse(string Value, string Label);

    private static void AssertClinicalValuesEmpty(AcceptedValuesResponse values)
    {
        Assert.Null(values.Duration);
        Assert.Null(values.Intensity);
        Assert.Null(values.AdditionalSymptoms);
    }
}
