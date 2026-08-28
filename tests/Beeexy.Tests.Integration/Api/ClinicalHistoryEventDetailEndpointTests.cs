using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class ClinicalHistoryEventDetailEndpointTests(
    PostgreSqlContainerFixture postgres)
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PatientEventScopingAndRelationshipRevocationAreConcealed()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var patientAClient = factory.CreateApiClient();
        using var patientBClient = factory.CreateApiClient();
        using var unrelatedClient = factory.CreateApiClient();
        var patientA = await AuthenticateAsync(factory, patientAClient, "detail-a");
        var patientB = await AuthenticateAsync(factory, patientBClient, "detail-b");
        var unrelated = await AuthenticateAsync(factory, unrelatedClient, "detail-unrelated");
        SetBearer(patientAClient, patientA.AccessToken);
        SetBearer(patientBClient, patientB.AccessToken);
        SetBearer(unrelatedClient, unrelated.AccessToken);
        var graphA = CreateGraph(EntityId.From(patientA.Account.ProfileId), 0);
        var graphB = CreateGraph(EntityId.From(patientB.Account.ProfileId), 10);
        var managed = PatientProfile.CreateManaged(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            PatientName.Create("Ana"),
            PatientName.Create("Rios"),
            new DateOnly(2011, 4, 5),
            SexAssignedAtBirth.Female,
            UsState.Create("NY"),
            Now);
        var managedGraph = CreateGraph(managed.Id, 20);
        var relationship = CareRelationship.Create(
            EntityId.From(patientA.Account.ProfileId),
            managed.Id,
            CareRelationshipType.Caregiver,
            EntityId.From(patientA.Account.AccountId),
            AuthorizationAttestation.Create("phase-5.4-detail", Now),
            Now);
        await SaveGraphAsync(graphA);
        await SaveGraphAsync(graphB);
        await using (var seed = CreateDbContext())
        {
            seed.Add(managed);
            seed.Add(relationship);
            AddGraph(seed, managedGraph);
            await seed.SaveChangesAsync();
        }

        using var own = await patientAClient.GetAsync(
            Endpoint(patientA.Account.ProfileId, graphA.HistoryEvent.Id.Value));
        using var managedResponse = await patientAClient.GetAsync(
            Endpoint(managed.Id.Value, managedGraph.HistoryEvent.Id.Value));
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.OK, managedResponse.StatusCode);

        using var crossPatient = await patientAClient.GetAsync(
            Endpoint(patientA.Account.ProfileId, graphB.HistoryEvent.Id.Value));
        using var absentEvent = await patientAClient.GetAsync(
            Endpoint(patientA.Account.ProfileId, Guid.NewGuid()));
        using var unrelatedPatient = await unrelatedClient.GetAsync(
            Endpoint(patientA.Account.ProfileId, graphA.HistoryEvent.Id.Value));
        using var missingPatient = await patientAClient.GetAsync(
            Endpoint(Guid.NewGuid(), graphA.HistoryEvent.Id.Value));
        var crossProblem = await crossPatient.Content.ReadFromJsonAsync<ProblemResponse>();
        var absentProblem = await absentEvent.Content.ReadFromJsonAsync<ProblemResponse>();
        var unrelatedProblem = await unrelatedPatient.Content.ReadFromJsonAsync<ProblemResponse>();
        var missingProblem = await missingPatient.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal(HttpStatusCode.NotFound, crossPatient.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absentEvent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unrelatedPatient.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingPatient.StatusCode);
        Assert.Equal(absentProblem, crossProblem);
        Assert.Equal(absentProblem, unrelatedProblem);
        Assert.Equal(absentProblem, missingProblem);

        using var sourceAsEvent = await patientAClient.GetAsync(
            Endpoint(patientA.Account.ProfileId, graphB.Episode.Id.Value));
        using var sourceAsPatient = await patientAClient.GetAsync(
            Endpoint(graphB.Episode.Id.Value, graphB.HistoryEvent.Id.Value));
        using var beeexyIdRoute = await patientAClient.GetAsync(
            $"/api/v1/patients/{patientA.Account.BeeexyId}/clinical-history/" +
            $"{graphA.HistoryEvent.Id.Value:D}");
        Assert.Equal(HttpStatusCode.NotFound, sourceAsEvent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sourceAsPatient.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, beeexyIdRoute.StatusCode);

        patientAClient.DefaultRequestHeaders.Authorization = null;
        using var missingBearer = await patientAClient.GetAsync(
            Endpoint(patientA.Account.ProfileId, graphA.HistoryEvent.Id.Value));
        SetBearer(patientAClient, "invalid-token");
        using var invalidBearer = await patientAClient.GetAsync(
            Endpoint(patientA.Account.ProfileId, graphA.HistoryEvent.Id.Value));
        Assert.Equal(HttpStatusCode.Unauthorized, missingBearer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidBearer.StatusCode);

        await using (var revoke = CreateDbContext())
        {
            var persisted = await revoke.CareRelationships.SingleAsync(
                candidate => candidate.Id == relationship.Id);
            persisted.Revoke(
                EntityId.From(patientA.Account.AccountId),
                Now.AddMinutes(30));
            await revoke.SaveChangesAsync();
        }

        SetBearer(patientAClient, patientA.AccessToken);
        using var revoked = await patientAClient.GetAsync(
            Endpoint(managed.Id.Value, managedGraph.HistoryEvent.Id.Value));
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
    }

    [Fact]
    public async Task DetailReturnsFrozenSourceAndOrderedAmendmentsWithoutMutatingRecords()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "detail-content");
        SetBearer(client, authentication.AccessToken);
        var patientId = EntityId.From(authentication.Account.ProfileId);
        var authorAccountId = EntityId.From(authentication.Account.AccountId);
        var graph = CreateGraph(patientId, 40);
        var otherGraph = CreateGraph(patientId, 50);
        var later = ClinicalAmendment.Create(
            graph.HistoryEvent,
            authorAccountId,
            AmendmentReason.Create("Second traceable correction"),
            graph.HistoryEvent.RecordedAt.AddMinutes(2));
        var earlier = ClinicalAmendment.Create(
            graph.HistoryEvent,
            authorAccountId,
            AmendmentReason.Create("First traceable correction"),
            graph.HistoryEvent.RecordedAt.AddMinutes(1));
        var unrelatedAmendment = ClinicalAmendment.Create(
            otherGraph.HistoryEvent,
            authorAccountId,
            AmendmentReason.Create("Another event correction"),
            otherGraph.HistoryEvent.RecordedAt.AddMinutes(1));
        var newerQuestionnaire = QuestionnaireDefinitionVersion.ImportApproved(
            graph.Questionnaire.QuestionnaireCode,
            DefinitionVersion.Create("detail-future"),
            DefinitionHash.FromHash(new string('c', 64)),
            Now.AddDays(1),
            Now.AddDays(1),
            Now.AddDays(1),
            questions:
            [
                new TriageQuestionInput(
                    QuestionCode.Create("FUTURE_DETAIL_ANSWER"),
                    "Future recorded answer",
                    1)
            ]);
        var newerRuleSet = ClinicalRuleSetVersion.ImportApproved(
            graph.RuleSet.RuleSetCode,
            DefinitionVersion.Create("detail-future"),
            DefinitionHash.FromHash(new string('d', 64)),
            Now.AddDays(1),
            Now.AddDays(1),
            Now.AddDays(1));
        await using (var seed = CreateDbContext())
        {
            AddGraph(seed, graph);
            AddGraph(seed, otherGraph);
            seed.AddRange(later, earlier, unrelatedAmendment);
            seed.AddRange(newerQuestionnaire, newerRuleSet);
            await seed.SaveChangesAsync();
        }

        var before = await LoadSnapshotAsync(graph);
        using var listResponse = await client.GetAsync(ListEndpoint(
            authentication.Account.ProfileId));
        var list = await listResponse.Content.ReadFromJsonAsync<HistoryPage>();
        using var detailResponse = await client.GetAsync(Endpoint(
            authentication.Account.ProfileId,
            graph.HistoryEvent.Id.Value));
        var detail = await detailResponse.Content.ReadFromJsonAsync<HistoryDetail>();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.NotNull(list);
        Assert.NotNull(detail);
        var listItem = Assert.Single(
            list.Items,
            item => item.EventId == graph.HistoryEvent.Id.Value);
        Assert.Equal(listItem.EventId, detail.EventId);
        Assert.Equal(listItem.EventType, detail.EventType);
        Assert.Equal(listItem.OccurredAt, detail.OccurredAt);
        Assert.Equal(listItem.RecordedAt, detail.RecordedAt);
        Assert.Equal(listItem.Source, detail.Source);
        Assert.Equal("COMPLETED_PRE_TRIAGE", detail.EventType);
        Assert.Equal("PRE_TRIAGE_EPISODE", detail.Source.Type);
        Assert.Equal(graph.Episode.Id.Value, detail.Source.Id);
        Assert.Equal(graph.Questionnaire.Id.Value, detail.Source.QuestionnaireVersionId);
        Assert.Equal(graph.RuleSet.Id.Value, detail.Source.ClinicalRuleSetVersionId);
        Assert.Equal("PRE_TRIAGE_EPISODE", detail.Provenance.SourceType);
        Assert.Equal(graph.Episode.Id.Value, detail.Provenance.SourceId);
        Assert.Equal(graph.Questionnaire.Id.Value,
            detail.Provenance.QuestionnaireVersionId);
        Assert.Equal(graph.RuleSet.Id.Value,
            detail.Provenance.ClinicalRuleSetVersionId);
        Assert.Equal(2, detail.Amendments.Count);
        Assert.Equal(
            [earlier.Id.Value, later.Id.Value],
            detail.Amendments.Select(item => item.AmendmentId));
        Assert.Equal(
            ["First traceable correction", "Second traceable correction"],
            detail.Amendments.Select(item => item.Reason));
        Assert.All(detail.Amendments, amendment =>
        {
            Assert.Equal("BEEEXY_ACCOUNT", amendment.Author.Type);
            Assert.Equal(authentication.Account.BeeexyId, amendment.Author.BeeexyId);
            Assert.Equal(graph.Episode.Id.Value, amendment.Provenance.SourceId);
            Assert.Equal(graph.Questionnaire.Id.Value,
                amendment.Provenance.QuestionnaireVersionId);
            Assert.Equal(graph.RuleSet.Id.Value,
                amendment.Provenance.ClinicalRuleSetVersionId);
        });
        Assert.DoesNotContain(
            detail.Amendments,
            item => item.AmendmentId == unrelatedAmendment.Id.Value);

        var after = await LoadSnapshotAsync(graph);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task CompletedPreTriageLifecycleProjectsToListAndMatchingDetail()
    {
        await EnsureMigratedAsync();
        var aiProvider = new FailIfInvokedClinicalAiProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<IClinicalAiProvider>();
                services.AddSingleton<IClinicalAiProvider>(aiProvider);
            });
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "detail-lifecycle");
        SetBearer(client, authentication.AccessToken);

        using var startResponse = await client.PostAsJsonAsync(
            "/api/v1/pre-triage/sessions",
            new { pathway = "HEADACHE" });
        var started = await startResponse.Content.ReadFromJsonAsync<StartedSession>();
        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);
        Assert.NotNull(started);
        using var answerResponse = await client.PostAsJsonAsync(
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/answers",
            new
            {
                structured = new
                {
                    duration = new { value = 2, unit = "DAYS" },
                    intensity = 7,
                    additionalSymptoms = new[] { "FEVER" }
                }
            });
        Assert.Equal(HttpStatusCode.OK, answerResponse.StatusCode);
        using var offerResponse = await client.PostAsJsonAsync(
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/educational-video-offer",
            new { decision = "SKIP" });
        Assert.Equal(HttpStatusCode.OK, offerResponse.StatusCode);
        using var completionResponse = await client.PostAsync(
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/complete",
            null);
        var completed = await completionResponse.Content
            .ReadFromJsonAsync<CompletedSession>();
        Assert.Equal(HttpStatusCode.Created, completionResponse.StatusCode);
        Assert.NotNull(completed);

        using var listResponse = await client.GetAsync(ListEndpoint(
            authentication.Account.ProfileId));
        var list = await listResponse.Content.ReadFromJsonAsync<HistoryPage>();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(list);
        var listItem = Assert.Single(list.Items);
        using var detailResponse = await client.GetAsync(Endpoint(
            authentication.Account.ProfileId,
            listItem.EventId));
        var detail = await detailResponse.Content.ReadFromJsonAsync<HistoryDetail>();

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.NotNull(detail);
        Assert.Equal(listItem.EventId, detail.EventId);
        Assert.Equal(listItem.EventType, detail.EventType);
        Assert.Equal(listItem.OccurredAt, detail.OccurredAt);
        Assert.Equal(listItem.RecordedAt, detail.RecordedAt);
        Assert.Equal(listItem.Source, detail.Source);
        Assert.Equal(completed.EpisodeId, detail.Source.Id);
        Assert.Equal(completed.CompletedAt, detail.OccurredAt);
        Assert.Equal(completed.PrimarySymptom, detail.PrimarySymptom);
        Assert.Equal(completed.Duration, detail.Duration);
        Assert.Equal(completed.Intensity, detail.Intensity);
        Assert.Equal(completed.AdditionalSymptoms, detail.AdditionalSymptoms!.ToArray());
        Assert.Empty(detail.Amendments);
        Assert.Equal(0, aiProvider.CallCount);
    }

    [Fact]
    public async Task MissingAuthoritativeEpisodeIsConcealedAsNotFound()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "detail-missing-source");
        SetBearer(client, authentication.AccessToken);
        var graph = CreateGraph(EntityId.From(authentication.Account.ProfileId), 65);
        await SaveGraphAsync(graph);

        await using (var corrupt = CreateDbContext())
        {
            await corrupt.Database.OpenConnectionAsync();
            try
            {
                await corrupt.Database.ExecuteSqlRawAsync(
                    "SET session_replication_role = replica");
                await corrupt.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE history.clinical_history_events SET source_id = {Guid.NewGuid()} WHERE id = {graph.HistoryEvent.Id.Value}");
            }
            finally
            {
                await corrupt.Database.ExecuteSqlRawAsync(
                    "SET session_replication_role = origin");
                await corrupt.Database.CloseConnectionAsync();
            }
        }

        using var response = await client.GetAsync(Endpoint(
            authentication.Account.ProfileId,
            graph.HistoryEvent.Id.Value));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Npgsql", await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EventWithoutAmendmentsReturnsAnEmptyCollectionAndMalformedRoutesAreSafe()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "detail-empty");
        SetBearer(client, authentication.AccessToken);
        var graph = CreateGraph(EntityId.From(authentication.Account.ProfileId), 70);
        await SaveGraphAsync(graph);

        using var response = await client.GetAsync(Endpoint(
            authentication.Account.ProfileId,
            graph.HistoryEvent.Id.Value));
        var detail = await response.Content.ReadFromJsonAsync<HistoryDetail>();
        using var malformedPatient = await client.GetAsync(
            $"/api/v1/patients/not-a-guid/clinical-history/{graph.HistoryEvent.Id.Value:D}");
        using var malformedEvent = await client.GetAsync(
            $"/api/v1/patients/{authentication.Account.ProfileId:D}/clinical-history/not-a-guid");
        using var emptyEvent = await client.GetAsync(Endpoint(
            authentication.Account.ProfileId,
            Guid.Empty));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(detail);
        Assert.Empty(detail.Amendments);
        Assert.Null(detail.PrimarySymptom);
        Assert.Null(detail.Duration);
        Assert.Null(detail.Intensity);
        Assert.Null(detail.AdditionalSymptoms);
        Assert.Equal(HttpStatusCode.NotFound, malformedPatient.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformedEvent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, emptyEvent.StatusCode);
        Assert.DoesNotContain("Npgsql", await emptyEvent.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    private HistoryGraph CreateGraph(EntityId patientId, int offset)
    {
        var questionnaire = CreateQuestionnaire(offset);
        var ruleSet = CreateRuleSet(offset);
        var createdAt = Now.AddMinutes(offset);
        var session = PreTriageSession.CreateForPatient(
            patientId,
            questionnaire.Id,
            createdAt.AddDays(1),
            createdAt);
        session.RecordAnswer(
            questionnaire.Questions.Single(),
            "{\"value\":\"recorded answer\"}",
            1,
            createdAt.AddMinutes(1));
        var episode = PreTriageEpisode.CreateFrom(
            session,
            ruleSet.Id,
            createdAt.AddMinutes(2));
        var assessment = ClinicalAssessment.CreateNeutral(
            episode,
            episode.CompletedAt);
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            episode,
            episode.CompletedAt.AddSeconds(5));
        return new HistoryGraph(
            patientId,
            questionnaire,
            ruleSet,
            session,
            episode,
            assessment,
            historyEvent);
    }

    private static QuestionnaireDefinitionVersion CreateQuestionnaire(int offset) =>
        QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create($"history-detail-{Guid.NewGuid():N}"),
            DefinitionVersion.Create($"detail-{offset}"),
            DefinitionHash.FromHash(new string('a', 64)),
            Now.AddMinutes(offset),
            Now.AddMinutes(offset),
            questions:
            [
                new TriageQuestionInput(
                    QuestionCode.Create("DETAIL_ANSWER"),
                    "Recorded answer",
                    1)
            ]);

    private static ClinicalRuleSetVersion CreateRuleSet(int offset) =>
        ClinicalRuleSetVersion.ImportApproved(
            RuleSetCode.Create($"history-detail-{Guid.NewGuid():N}"),
            DefinitionVersion.Create($"detail-{offset}"),
            DefinitionHash.FromHash(new string('b', 64)),
            Now.AddMinutes(offset),
            Now.AddMinutes(offset));

    private async Task SaveGraphAsync(HistoryGraph graph)
    {
        await using var dbContext = CreateDbContext();
        AddGraph(dbContext, graph);
        await dbContext.SaveChangesAsync();
    }

    private static void AddGraph(BeeexyDbContext dbContext, HistoryGraph graph) =>
        dbContext.AddRange(
            graph.Questionnaire,
            graph.RuleSet,
            graph.Session,
            graph.Episode,
            graph.Assessment,
            graph.HistoryEvent);

    private async Task<HistorySnapshot> LoadSnapshotAsync(HistoryGraph graph)
    {
        await using var dbContext = CreateDbContext();
        var episode = await dbContext.PreTriageEpisodes
            .AsNoTracking()
            .SingleAsync(item => item.Id == graph.Episode.Id);
        var assessment = await dbContext.ClinicalAssessments
            .AsNoTracking()
            .SingleAsync(item => item.EpisodeId == graph.Episode.Id);
        var answers = await dbContext.TriageAnswers
            .AsNoTracking()
            .Where(item => item.EpisodeId == graph.Episode.Id)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.AnswerJson,
                item.RecordedAt
            })
            .ToArrayAsync();
        var historyEvent = await dbContext.ClinicalHistoryEvents
            .AsNoTracking()
            .SingleAsync(item => item.Id == graph.HistoryEvent.Id);
        var amendments = await dbContext.ClinicalAmendments
            .AsNoTracking()
            .Where(item => item.ClinicalHistoryEventId == graph.HistoryEvent.Id)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                Reason = item.Reason.Value,
                item.CreatedAt,
                item.SourceId,
                item.SourceQuestionnaireVersionId,
                item.SourceClinicalRuleSetVersionId
            })
            .ToArrayAsync();
        return new HistorySnapshot(
            $"{episode.Id}|{episode.PatientProfileId}|{episode.QuestionnaireVersionId}|" +
            $"{episode.ClinicalRuleSetVersionId}|{episode.CompletedAt:O}",
            $"{assessment.Id}|{assessment.UrgencyCode}|{assessment.CreatedAt:O}",
            string.Join(';', answers.Select(item =>
                $"{item.Id}|{item.AnswerJson}|{item.RecordedAt:O}")),
            $"{historyEvent.Id}|{historyEvent.PatientProfileId}|{historyEvent.SourceId}|" +
            $"{historyEvent.OccurredAt:O}|{historyEvent.RecordedAt:O}",
            string.Join(';', amendments.Select(item =>
                $"{item.Id}|{item.Reason}|{item.CreatedAt:O}|{item.SourceId}|" +
                $"{item.SourceQuestionnaireVersionId}|" +
                $"{item.SourceClinicalRuleSetVersionId}")));
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
            candidate => candidate.Recipient.Value == email);
        using var verification = await client.PostAsJsonAsync(
            "/api/v1/auth/email/verify",
            new { email, code = message.OneTimeCode });
        verification.EnsureSuccessStatusCode();
        return Assert.IsType<AuthenticationResult>(
            await verification.Content.ReadFromJsonAsync<AuthenticationResult>());
    }

    private static string ListEndpoint(Guid patientId) =>
        $"/api/v1/patients/{patientId:D}/clinical-history";

    private static string Endpoint(Guid patientId, Guid eventId) =>
        $"{ListEndpoint(patientId)}/{eventId:D}";

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
        EntityId PatientId,
        QuestionnaireDefinitionVersion Questionnaire,
        ClinicalRuleSetVersion RuleSet,
        PreTriageSession Session,
        PreTriageEpisode Episode,
        ClinicalAssessment Assessment,
        ClinicalHistoryEvent HistoryEvent);

    private sealed record HistorySnapshot(
        string Episode,
        string Assessment,
        string Answers,
        string HistoryEvent,
        string Amendments);

    private sealed record AuthenticationResult(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);

    private sealed record StartedSession(Guid SessionId);

    private sealed record CompletedSession(
        Guid EpisodeId,
        DateTimeOffset CompletedAt,
        HistoryPrimarySymptom PrimarySymptom,
        HistoryDuration Duration,
        int Intensity,
        IReadOnlyList<string> AdditionalSymptoms);

    private sealed record HistoryPage(
        IReadOnlyList<HistoryItem> Items,
        string? NextCursor);

    private sealed record HistoryDetail(
        Guid EventId,
        string EventType,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        HistorySource Source,
        HistoryProvenance Provenance,
        HistoryPrimarySymptom? PrimarySymptom,
        HistoryDuration? Duration,
        int? Intensity,
        IReadOnlyList<string>? AdditionalSymptoms,
        IReadOnlyList<HistoryAmendment> Amendments);

    private sealed record HistoryPrimarySymptom(string Code, string Display);

    private sealed record HistoryDuration(decimal Value, string Unit);

    private sealed record HistoryItem(
        Guid EventId,
        string EventType,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        HistorySource Source);

    private sealed record HistorySource(
        string Type,
        Guid Id,
        Guid QuestionnaireVersionId,
        Guid ClinicalRuleSetVersionId);

    private sealed record HistoryProvenance(
        string SourceType,
        Guid SourceId,
        Guid QuestionnaireVersionId,
        Guid ClinicalRuleSetVersionId);

    private sealed record HistoryAmendment(
        Guid AmendmentId,
        string Reason,
        HistoryAuthor Author,
        DateTimeOffset CreatedAt,
        HistoryProvenance Provenance);

    private sealed record HistoryAuthor(string Type, string? BeeexyId);

    private sealed record ProblemResponse(
        int Status,
        string Title,
        string? Type,
        string? Detail,
        string? ErrorCode);

    private sealed class FailIfInvokedClinicalAiProvider : IClinicalAiProvider
    {
        public int CallCount { get; private set; }

        public Task<ClinicalAiProviderOutput> InterpretAsync(
            ClinicalAiInterpretationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException(
                "Clinical History detail must not invoke Clinical AI.");
        }
    }
}
