using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class PreTriageAmendmentEndpointTests(
    PostgreSqlContainerFixture postgres)
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 23, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompletedPrimaryJourneyCreatesTraceableAmendmentWithoutSourceMutation()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "amend-primary");

        using var missingBearer = await client.PostAsJsonAsync(
            Endpoint(Guid.NewGuid()),
            ValidRequest());
        SetBearer(client, "not-a-valid-token");
        using var invalidBearer = await client.PostAsJsonAsync(
            Endpoint(Guid.NewGuid()),
            ValidRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, missingBearer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidBearer.StatusCode);

        SetBearer(client, authentication.AccessToken);
        var completed = await CompleteAuthenticatedJourneyAsync(client);
        var originalBefore = await LoadOriginalSnapshotAsync(completed.EpisodeId);
        var listEndpoint =
            $"/api/v1/patients/{authentication.Account.ProfileId:D}/clinical-history";
        using var listBeforeResponse = await client.GetAsync(listEndpoint);
        var listBefore = await listBeforeResponse.Content.ReadAsStringAsync();

        var invalidRequests = new object[]
        {
            new { },
            new { idempotencyKey = "not-a-uuid", reason = "Correction reason" },
            new { idempotencyKey = Guid.NewGuid(), reason = "  " }
        };
        foreach (var invalidRequest in invalidRequests)
        {
            using var invalid = await client.PostAsJsonAsync(
                Endpoint(completed.EpisodeId),
                invalidRequest);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        }

        using var forgedAudit = await client.PostAsJsonAsync(
            Endpoint(completed.EpisodeId),
            new
            {
                idempotencyKey = Guid.NewGuid(),
                reason = "Correction reason",
                amendmentId = Guid.NewGuid(),
                authorAccountId = Guid.NewGuid(),
                createdAt = Now.AddYears(-1),
                correction = new { urgency = "HIGH" }
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, forgedAudit.StatusCode);
        Assert.Equal(0, await CountAmendmentsAsync(completed.EpisodeId));

        var key = Guid.NewGuid();
        var requestStartedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        using var response = await client.PostAsJsonAsync(
            Endpoint(completed.EpisodeId),
            new { idempotencyKey = key, reason = "  Correct reported duration  " });
        var requestFinishedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        var amendment = await response.Content.ReadFromJsonAsync<AmendmentResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(amendment);
        Assert.NotEqual(Guid.Empty, amendment.AmendmentId);
        Assert.Equal("Correct reported duration", amendment.Reason);
        Assert.Equal("BEEEXY_ACCOUNT", amendment.Author.Type);
        Assert.Equal(authentication.Account.BeeexyId, amendment.Author.BeeexyId);
        Assert.InRange(amendment.CreatedAt, requestStartedAt, requestFinishedAt);
        Assert.Equal("PRE_TRIAGE_EPISODE", amendment.Provenance.SourceType);
        Assert.Equal(completed.EpisodeId, amendment.Provenance.SourceId);
        Assert.NotNull(response.Headers.Location);

        await using (var dbContext = CreateDbContext())
        {
            var persisted = await dbContext.ClinicalAmendments
                .AsNoTracking()
                .SingleAsync(item => item.Id == EntityId.From(amendment.AmendmentId));
            Assert.Equal(EntityId.From(authentication.Account.AccountId),
                persisted.AuthorAccountId);
            Assert.Equal(EntityId.From(key), persisted.IdempotencyKey);
            Assert.Equal(amendment.CreatedAt, persisted.CreatedAt);
            Assert.Equal(completed.EpisodeId, persisted.SourceId.Value);
        }

        Assert.Equal(originalBefore, await LoadOriginalSnapshotAsync(completed.EpisodeId));
        using var listAfterResponse = await client.GetAsync(listEndpoint);
        Assert.Equal(listBefore, await listAfterResponse.Content.ReadAsStringAsync());

        await using var read = CreateDbContext();
        var eventId = await read.ClinicalHistoryEvents
            .Where(item => item.SourceId == EntityId.From(completed.EpisodeId))
            .Select(item => item.Id.Value)
            .SingleAsync();
        using var detailResponse = await client.GetAsync(
            $"{listEndpoint}/{eventId:D}");
        var detail = await detailResponse.Content.ReadFromJsonAsync<HistoryDetail>();
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.NotNull(detail);
        Assert.Equal(amendment, Assert.Single(detail.Amendments));
    }

    [Fact]
    public async Task ActiveManagerCanAmendThenRevocationAndCrossPatientKnowledgeAreConcealed()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var managerClient = factory.CreateApiClient();
        using var unrelatedClient = factory.CreateApiClient();
        var manager = await AuthenticateAsync(factory, managerClient, "amend-manager");
        var unrelated = await AuthenticateAsync(factory, unrelatedClient, "amend-unrelated");
        SetBearer(managerClient, manager.AccessToken);
        SetBearer(unrelatedClient, unrelated.AccessToken);
        var managedPatient = PatientProfile.CreateManaged(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            PatientName.Create("Ana"),
            PatientName.Create("Rios"),
            new DateOnly(2010, 2, 3),
            SexAssignedAtBirth.Female,
            UsState.Create("NY"),
            Now);
        var relationship = CareRelationship.Create(
            EntityId.From(manager.Account.ProfileId),
            managedPatient.Id,
            CareRelationshipType.Caregiver,
            EntityId.From(manager.Account.AccountId),
            AuthorizationAttestation.Create("phase-5.5-amend", Now),
            Now);
        var graph = CreateGraph(managedPatient.Id, 10);
        await using (var seed = CreateDbContext())
        {
            seed.AddRange(managedPatient, relationship);
            AddGraph(seed, graph, includeHistoryEvent: true);
            await seed.SaveChangesAsync();
        }

        using var created = await managerClient.PostAsJsonAsync(
            Endpoint(graph.Episode.Id.Value),
            ValidRequest("Manager traceable reason"));
        var createdBody = await created.Content.ReadFromJsonAsync<AmendmentResponse>();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(manager.Account.BeeexyId, createdBody!.Author.BeeexyId);

        using var crossPatient = await unrelatedClient.PostAsJsonAsync(
            Endpoint(graph.Episode.Id.Value),
            ValidRequest());
        using var absent = await unrelatedClient.PostAsJsonAsync(
            Endpoint(Guid.NewGuid()),
            ValidRequest());
        using var eventIdAsEpisode = await managerClient.PostAsJsonAsync(
            Endpoint(graph.HistoryEvent.Id.Value),
            ValidRequest());
        var crossProblem = await crossPatient.Content.ReadFromJsonAsync<ProblemResponse>();
        var absentProblem = await absent.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal(HttpStatusCode.NotFound, crossPatient.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(absentProblem, crossProblem);
        Assert.Equal(HttpStatusCode.NotFound, eventIdAsEpisode.StatusCode);

        await using (var revoke = CreateDbContext())
        {
            var persisted = await revoke.CareRelationships.SingleAsync(
                item => item.Id == relationship.Id);
            persisted.Revoke(
                EntityId.From(manager.Account.AccountId),
                Now.AddMinutes(30));
            await revoke.SaveChangesAsync();
        }

        using var revoked = await managerClient.PostAsJsonAsync(
            Endpoint(graph.Episode.Id.Value),
            ValidRequest("Reason after revocation"));
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.Equal(1, await CountAmendmentsAsync(graph.Episode.Id.Value));

        await using var verify = CreateDbContext();
        var first = await verify.ClinicalAmendments.AsNoTracking().SingleAsync(
            item => item.SourceId == graph.Episode.Id);
        Assert.Equal(EntityId.From(manager.Account.AccountId), first.AuthorAccountId);
    }

    [Fact]
    public async Task DatabaseBackedIdempotencyHandlesSequentialAndConcurrentRequests()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "amend-idempotency");
        SetBearer(client, authentication.AccessToken);
        var graph = CreateGraph(EntityId.From(authentication.Account.ProfileId), 30);
        await using (var seed = CreateDbContext())
        {
            AddGraph(seed, graph, includeHistoryEvent: true);
            await seed.SaveChangesAsync();
        }

        var firstKey = Guid.NewGuid();
        using var first = await client.PostAsJsonAsync(
            Endpoint(graph.Episode.Id.Value),
            ValidRequest("Original retry metadata", firstKey));
        var firstBody = await first.Content.ReadFromJsonAsync<AmendmentResponse>();
        using var duplicate = await client.PostAsJsonAsync(
            Endpoint(graph.Episode.Id.Value),
            ValidRequest("Attempted metadata replacement", firstKey));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var distinctA = await client.PostAsJsonAsync(
            Endpoint(graph.Episode.Id.Value),
            ValidRequest("Distinct amendment A"));
        using var distinctB = await client.PostAsJsonAsync(
            Endpoint(graph.Episode.Id.Value),
            ValidRequest("Distinct amendment B"));
        Assert.Equal(HttpStatusCode.Created, distinctA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, distinctB.StatusCode);

        var concurrentKey = Guid.NewGuid();
        var concurrent = await Task.WhenAll(
            client.PostAsJsonAsync(
                Endpoint(graph.Episode.Id.Value),
                ValidRequest("Concurrent retry", concurrentKey)),
            client.PostAsJsonAsync(
                Endpoint(graph.Episode.Id.Value),
                ValidRequest("Concurrent retry", concurrentKey)));
        try
        {
            Assert.Equal(
                [HttpStatusCode.Created, HttpStatusCode.Conflict],
                concurrent.Select(item => item.StatusCode)
                    .OrderBy(item => (int)item));
        }
        finally
        {
            foreach (var response in concurrent)
            {
                response.Dispose();
            }
        }

        await using var dbContext = CreateDbContext();
        var persisted = await dbContext.ClinicalAmendments
            .AsNoTracking()
            .Where(item => item.SourceId == graph.Episode.Id)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToArrayAsync();
        Assert.Equal(4, persisted.Length);
        var original = Assert.Single(persisted,
            item => item.IdempotencyKey == EntityId.From(firstKey));
        Assert.Equal(firstBody!.AmendmentId, original.Id.Value);
        Assert.Equal("Original retry metadata", original.Reason.Value);
        Assert.Equal(firstBody.CreatedAt, original.CreatedAt);
        Assert.Single(persisted,
            item => item.IdempotencyKey == EntityId.From(concurrentKey));
    }

    [Fact]
    public async Task IneligibleSourcesAreRejectedAndClaimedAnonymousCompletionBecomesAmendable()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var authenticatedClient = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(
            factory,
            authenticatedClient,
            "amend-claim");
        SetBearer(authenticatedClient, authentication.AccessToken);
        var noHistory = CreateGraph(EntityId.From(authentication.Account.ProfileId), 50);
        await using (var seed = CreateDbContext())
        {
            AddGraph(seed, noHistory, includeHistoryEvent: false);
            await seed.SaveChangesAsync();
        }

        using var noHistoryResponse = await authenticatedClient.PostAsJsonAsync(
            Endpoint(noHistory.Episode.Id.Value),
            ValidRequest());
        using var sessionIdOnly = await authenticatedClient.PostAsJsonAsync(
            Endpoint(noHistory.Session.Id.Value),
            ValidRequest());
        Assert.Equal(HttpStatusCode.NotFound, noHistoryResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sessionIdOnly.StatusCode);

        using var anonymousClient = factory.CreateApiClient();
        var anonymous = await StartJourneyAsync(anonymousClient);
        await AnswerJourneyAsync(anonymousClient, anonymous, anonymousCapability: true);
        var completed = await CompleteJourneyAsync(
            anonymousClient,
            anonymous,
            anonymousCapability: true);
        using var unclaimed = await authenticatedClient.PostAsJsonAsync(
            Endpoint(completed.EpisodeId),
            ValidRequest());
        Assert.Equal(HttpStatusCode.NotFound, unclaimed.StatusCode);

        using var claimRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/pre-triage/sessions/{anonymous.SessionId:D}/claim");
        claimRequest.Headers.Add("X-Pre-Triage-Capability", anonymous.AnonymousCapability);
        using var claimed = await authenticatedClient.SendAsync(claimRequest);
        Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);

        using var amendClaimed = await authenticatedClient.PostAsJsonAsync(
            Endpoint(completed.EpisodeId),
            ValidRequest("Post-claim correction reason"));
        Assert.Equal(HttpStatusCode.Created, amendClaimed.StatusCode);
        Assert.Equal(1, await CountAmendmentsAsync(completed.EpisodeId));
    }

    private async Task<CompletedJourney> CompleteAuthenticatedJourneyAsync(HttpClient client)
    {
        var started = await StartJourneyAsync(client);
        await AnswerJourneyAsync(client, started, anonymousCapability: false);
        return await CompleteJourneyAsync(client, started, anonymousCapability: false);
    }

    private static async Task<StartedJourney> StartJourneyAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/pre-triage/sessions",
            new { pathway = "HEADACHE" });
        var started = await response.Content.ReadFromJsonAsync<StartedJourney>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<StartedJourney>(started);
    }

    private static async Task AnswerJourneyAsync(
        HttpClient client,
        StartedJourney started,
        bool anonymousCapability)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/answers")
        {
            Content = JsonContent.Create(new
            {
                structured = new
                {
                    duration = new { value = 2, unit = "DAYS" },
                    intensity = 6,
                    additionalSymptoms = new[] { "FEVER" }
                }
            })
        };
        if (anonymousCapability)
        {
            request.Headers.Add("X-Pre-Triage-Capability", started.AnonymousCapability);
        }

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<CompletedJourney> CompleteJourneyAsync(
        HttpClient client,
        StartedJourney started,
        bool anonymousCapability)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/complete");
        if (anonymousCapability)
        {
            request.Headers.Add("X-Pre-Triage-Capability", started.AnonymousCapability);
        }

        using var response = await client.SendAsync(request);
        var completed = await response.Content.ReadFromJsonAsync<CompletedJourney>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<CompletedJourney>(completed);
    }

    private HistoryGraph CreateGraph(EntityId patientId, int offset)
    {
        var questionnaire = QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create($"amend-{Guid.NewGuid():N}"),
            DefinitionVersion.Create($"phase-55-{offset}"),
            DefinitionHash.FromHash(new string('a', 64)),
            Now.AddMinutes(offset),
            Now.AddMinutes(offset),
            questions:
            [
                new TriageQuestionInput(
                    QuestionCode.Create("AMENDMENT_ANSWER"),
                    "Recorded answer",
                    1)
            ]);
        var ruleSet = ClinicalRuleSetVersion.ImportApproved(
            RuleSetCode.Create($"amend-{Guid.NewGuid():N}"),
            DefinitionVersion.Create($"phase-55-{offset}"),
            DefinitionHash.FromHash(new string('b', 64)),
            Now.AddMinutes(offset),
            Now.AddMinutes(offset));
        var session = PreTriageSession.CreateForPatient(
            patientId,
            questionnaire.Id,
            Now.AddDays(1),
            Now.AddMinutes(offset));
        session.RecordAnswer(
            questionnaire.Questions.Single(),
            "{\"value\":\"unchanged\"}",
            1,
            Now.AddMinutes(offset + 1));
        var episode = PreTriageEpisode.CreateFrom(
            session,
            ruleSet.Id,
            Now.AddMinutes(offset + 2));
        var assessment = ClinicalAssessment.CreateNeutral(episode, episode.CompletedAt);
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            episode,
            episode.CompletedAt.AddSeconds(1));
        return new HistoryGraph(
            questionnaire,
            ruleSet,
            session,
            episode,
            assessment,
            historyEvent);
    }

    private static void AddGraph(
        BeeexyDbContext dbContext,
        HistoryGraph graph,
        bool includeHistoryEvent)
    {
        dbContext.AddRange(
            graph.Questionnaire,
            graph.RuleSet,
            graph.Session,
            graph.Episode,
            graph.Assessment);
        if (includeHistoryEvent)
        {
            dbContext.Add(graph.HistoryEvent);
        }
    }

    private async Task<OriginalSnapshot> LoadOriginalSnapshotAsync(Guid episodeId)
    {
        await using var dbContext = CreateDbContext();
        var episode = await dbContext.PreTriageEpisodes
            .AsNoTracking()
            .Include(item => item.Answers)
            .Include(item => item.ReportedSymptoms)
            .AsSplitQuery()
            .SingleAsync(item => item.Id == EntityId.From(episodeId));
        var assessment = await dbContext.ClinicalAssessments
            .AsNoTracking()
            .Include(item => item.Findings)
            .SingleAsync(item => item.EpisodeId == episode.Id);
        var historyEvent = await dbContext.ClinicalHistoryEvents
            .AsNoTracking()
            .SingleAsync(item => item.SourceId == episode.Id);
        return new OriginalSnapshot(
            $"{episode.Id}|{episode.SourceSessionId}|{episode.PatientProfileId}|" +
            $"{episode.QuestionnaireVersionId}|{episode.ClinicalRuleSetVersionId}|" +
            $"{episode.CompletedAt:O}|{episode.ClaimedAt:O}",
            string.Join(';', episode.Answers.OrderBy(item => item.Id.Value).Select(item =>
                $"{item.Id}|{item.AnswerJson}|{item.RecordedAt:O}")),
            string.Join(';', episode.ReportedSymptoms.OrderBy(item => item.Id.Value).Select(item =>
                $"{item.Id}|{item.OriginalText.Value}|{item.ReportedAt:O}")),
            $"{assessment.Id}|{assessment.ClinicalRuleSetVersionId}|" +
            $"{assessment.UrgencyCode?.Value}|{assessment.CreatedAt:O}|" +
            $"{assessment.Findings.Count}",
            $"{historyEvent.Id}|{historyEvent.PatientProfileId}|" +
            $"{historyEvent.SourceId}|{historyEvent.OccurredAt:O}|" +
            $"{historyEvent.RecordedAt:O}|{historyEvent.SourceQuestionnaireVersionId}|" +
            $"{historyEvent.SourceClinicalRuleSetVersionId}");
    }

    private async Task<int> CountAmendmentsAsync(Guid episodeId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.ClinicalAmendments.CountAsync(
            item => item.SourceId == EntityId.From(episodeId));
    }

    private async Task<AuthenticationResult> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client,
        string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        using var challenge = await client.PostAsJsonAsync(
            "/api/v1/auth/email/challenges",
            new { email });
        Assert.Equal(HttpStatusCode.Accepted, challenge.StatusCode);
        var message = Assert.Single(
            factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>().Messages,
            item => item.Recipient.Value == email);
        using var verification = await client.PostAsJsonAsync(
            "/api/v1/auth/email/verify",
            new { email, code = message.OneTimeCode });
        verification.EnsureSuccessStatusCode();
        return Assert.IsType<AuthenticationResult>(
            await verification.Content.ReadFromJsonAsync<AuthenticationResult>());
    }

    private static object ValidRequest(
        string reason = "Traceable correction reason",
        Guid? key = null) =>
        new { idempotencyKey = key ?? Guid.NewGuid(), reason };

    private static string Endpoint(Guid episodeId) =>
        $"/api/v1/pre-triage/episodes/{episodeId:D}/amendments";

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

    private BeeexyDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private sealed record HistoryGraph(
        QuestionnaireDefinitionVersion Questionnaire,
        ClinicalRuleSetVersion RuleSet,
        PreTriageSession Session,
        PreTriageEpisode Episode,
        ClinicalAssessment Assessment,
        ClinicalHistoryEvent HistoryEvent);

    private sealed record OriginalSnapshot(
        string Episode,
        string Answers,
        string Symptoms,
        string Assessment,
        string HistoryEvent);

    private sealed record AuthenticationResult(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);

    private sealed record StartedJourney(
        Guid SessionId,
        string? AnonymousCapability);

    private sealed record CompletedJourney(
        Guid EpisodeId,
        DateTimeOffset CompletedAt);

    private sealed record AmendmentResponse(
        Guid AmendmentId,
        string Reason,
        AmendmentAuthor Author,
        DateTimeOffset CreatedAt,
        AmendmentProvenance Provenance);

    private sealed record AmendmentAuthor(string Type, string? BeeexyId);

    private sealed record AmendmentProvenance(
        string SourceType,
        Guid SourceId,
        Guid QuestionnaireVersionId,
        Guid ClinicalRuleSetVersionId);

    private sealed record HistoryDetail(IReadOnlyList<AmendmentResponse> Amendments);

    private sealed record ProblemResponse(
        int Status,
        string Title,
        string? Type,
        string? Detail,
        string? ErrorCode);
}
