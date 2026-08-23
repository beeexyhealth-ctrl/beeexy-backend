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
public sealed class ClinicalHistoryListEndpointTests(PostgreSqlContainerFixture postgres)
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BearerAuthorizationConcealsDeniedPatientsAndAppliesRevocationImmediately()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var manager = await AuthenticateAsync(factory, client, "history-manager");
        using var unrelatedClient = factory.CreateApiClient();
        var unrelated = await AuthenticateAsync(
            factory,
            unrelatedClient,
            "history-unrelated");
        SetBearer(client, manager.AccessToken);

        using var ownResponse = await client.GetAsync(Endpoint(manager.Account.ProfileId));
        var ownPage = await ownResponse.Content.ReadFromJsonAsync<HistoryPage>();
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.NotNull(ownPage);
        Assert.Empty(ownPage.Items);
        Assert.Null(ownPage.NextCursor);

        var managedPatient = PatientProfile.CreateManaged(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            PatientName.Create("Maria"),
            PatientName.Create("Arias"),
            new DateOnly(2012, 5, 12),
            SexAssignedAtBirth.Female,
            UsState.Create("NY"),
            Now);
        var relationship = CareRelationship.Create(
            EntityId.From(manager.Account.ProfileId),
            managedPatient.Id,
            CareRelationshipType.Caregiver,
            EntityId.From(manager.Account.AccountId),
            AuthorizationAttestation.Create("phase-5.3-test", Now),
            Now);
        await using (var seed = CreateDbContext())
        {
            seed.AddRange(managedPatient, relationship);
            await seed.SaveChangesAsync();
        }

        using var activeResponse = await client.GetAsync(Endpoint(managedPatient.Id.Value));
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);

        await using (var revoke = CreateDbContext())
        {
            var persisted = await revoke.CareRelationships.SingleAsync(
                candidate => candidate.Id == relationship.Id);
            persisted.Revoke(EntityId.From(manager.Account.AccountId), Now.AddMinutes(1));
            await revoke.SaveChangesAsync();
        }

        using var revokedResponse = await client.GetAsync(
            Endpoint(managedPatient.Id.Value) + "?eventType=COMPLETED_PRE_TRIAGE");
        using var unrelatedResponse = await client.GetAsync(
            Endpoint(unrelated.Account.ProfileId));
        using var missingResponse = await client.GetAsync(Endpoint(Guid.NewGuid()));
        var revokedProblem = await revokedResponse.Content.ReadFromJsonAsync<ProblemResponse>();
        var unrelatedProblem = await unrelatedResponse.Content.ReadFromJsonAsync<ProblemResponse>();
        var missingProblem = await missingResponse.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal(HttpStatusCode.NotFound, revokedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unrelatedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(missingProblem, revokedProblem);
        Assert.Equal(missingProblem, unrelatedProblem);

        client.DefaultRequestHeaders.Authorization = null;
        using var missingBearer = await client.GetAsync(Endpoint(manager.Account.ProfileId));
        Assert.Equal(HttpStatusCode.Unauthorized, missingBearer.StatusCode);
        SetBearer(client, "not-a-valid-token");
        using var invalidBearer = await client.GetAsync(Endpoint(manager.Account.ProfileId));
        Assert.Equal(HttpStatusCode.Unauthorized, invalidBearer.StatusCode);
        SetBearer(client, manager.AccessToken);
        using var beeexyIdRoute = await client.GetAsync(
            $"/api/v1/patients/{manager.Account.BeeexyId}/clinical-history");
        Assert.Equal(HttpStatusCode.NotFound, beeexyIdRoute.StatusCode);
    }

    [Fact]
    public async Task ListReturnsOnlyProjectedEventWithFrozenSourceMetadataAndDoesNotWrite()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "history-projection");
        SetBearer(client, authentication.AccessToken);
        var graph = CreateHistoryGraph(
            EntityId.From(authentication.Account.ProfileId),
            eventCount: 2,
            projectIndexes: [0]);
        await SaveHistoryGraphAsync(graph);

        int eventCountBefore;
        int episodeCountBefore;
        await using (var before = CreateDbContext())
        {
            eventCountBefore = await before.ClinicalHistoryEvents.CountAsync(
                item => item.PatientProfileId == graph.PatientProfileId);
            episodeCountBefore = await before.PreTriageEpisodes.CountAsync(
                item => item.PatientProfileId == graph.PatientProfileId);
        }

        using var response = await client.GetAsync(Endpoint(authentication.Account.ProfileId));
        var page = await response.Content.ReadFromJsonAsync<HistoryPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        var item = Assert.Single(page.Items);
        var projected = Assert.Single(graph.HistoryEvents);
        Assert.Equal(projected.Id.Value, item.EventId);
        Assert.Equal("COMPLETED_PRE_TRIAGE", item.EventType);
        Assert.Equal(projected.OccurredAt, item.OccurredAt);
        Assert.Equal(projected.RecordedAt, item.RecordedAt);
        Assert.Equal("PRE_TRIAGE_EPISODE", item.Source.Type);
        Assert.Equal(projected.SourceId.Value, item.Source.Id);
        Assert.Equal(projected.SourceQuestionnaireVersionId.Value,
            item.Source.QuestionnaireVersionId);
        Assert.Equal(projected.SourceClinicalRuleSetVersionId.Value,
            item.Source.ClinicalRuleSetVersionId);
        Assert.Null(page.NextCursor);

        using var sourceIdAsPatient = await client.GetAsync(
            Endpoint(projected.SourceId.Value));
        Assert.Equal(HttpStatusCode.NotFound, sourceIdAsPatient.StatusCode);

        await using var after = CreateDbContext();
        Assert.Equal(eventCountBefore, await after.ClinicalHistoryEvents.CountAsync(
            candidate => candidate.PatientProfileId == graph.PatientProfileId));
        Assert.Equal(episodeCountBefore, await after.PreTriageEpisodes.CountAsync(
            candidate => candidate.PatientProfileId == graph.PatientProfileId));
    }

    [Fact]
    public async Task KeysetCursorTraversesMoreThanTenEventsWithoutGapsOrDuplicates()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "history-pagination");
        SetBearer(client, authentication.AccessToken);
        var graph = CreateHistoryGraph(
            EntityId.From(authentication.Account.ProfileId),
            eventCount: 13,
            projectIndexes: Enumerable.Range(0, 13).ToArray(),
            deterministicEventIds: true);
        await SaveHistoryGraphAsync(graph);
        var expected = graph.HistoryEvents
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id.Value.ToString("D"), StringComparer.Ordinal)
            .Select(item => item.Id.Value)
            .ToArray();

        var observed = new List<Guid>();
        string? cursor = null;
        do
        {
            var uri = Endpoint(authentication.Account.ProfileId) +
                "?pageSize=4&eventType=COMPLETED_PRE_TRIAGE" +
                (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            using var response = await client.GetAsync(uri);
            var page = await response.Content.ReadFromJsonAsync<HistoryPage>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(page);
            Assert.InRange(page.Items.Count, 1, 4);
            observed.AddRange(page.Items.Select(item => item.EventId));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(13, observed.Count);
        Assert.Equal(13, observed.Distinct().Count());
        Assert.Equal(expected, observed);

        using var firstResponse = await client.GetAsync(
            Endpoint(authentication.Account.ProfileId) + "?pageSize=1");
        var firstPage = await firstResponse.Content.ReadFromJsonAsync<HistoryPage>();
        Assert.NotNull(firstPage);
        Assert.NotNull(firstPage.NextCursor);

        using var malformed = await client.GetAsync(
            Endpoint(authentication.Account.ProfileId) + "?cursor=not-a-cursor");
        using var wrongFilter = await client.GetAsync(
            Endpoint(authentication.Account.ProfileId) +
            $"?eventType=COMPLETED_PRE_TRIAGE&cursor={firstPage.NextCursor}");
        using var unsupportedFilter = await client.GetAsync(
            Endpoint(authentication.Account.ProfileId) + "?eventType=UNKNOWN");
        using var invalidPageSize = await client.GetAsync(
            Endpoint(authentication.Account.ProfileId) + "?pageSize=101");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongFilter.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsupportedFilter.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidPageSize.StatusCode);

        using var secondClient = factory.CreateApiClient();
        var second = await AuthenticateAsync(factory, secondClient, "history-cursor-context");
        var secondHistory = CreateHistoryGraph(
            EntityId.From(second.Account.ProfileId),
            eventCount: 1,
            projectIndexes: [0],
            occurrenceMinuteOffset: -10);
        await SaveHistoryGraphAsync(secondHistory);
        var relationship = CareRelationship.Create(
            EntityId.From(authentication.Account.ProfileId),
            EntityId.From(second.Account.ProfileId),
            CareRelationshipType.Caregiver,
            EntityId.From(authentication.Account.AccountId),
            AuthorizationAttestation.Create("phase-5.3-cursor-test", Now),
            Now);
        await using (var seed = CreateDbContext())
        {
            seed.Add(relationship);
            await seed.SaveChangesAsync();
        }

        using var crossPatient = await client.GetAsync(
            Endpoint(second.Account.ProfileId) + $"?cursor={firstPage.NextCursor}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, crossPatient.StatusCode);
        var problem = await crossPatient.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("clinical_history.cursor_invalid", problem!.ErrorCode);
    }

    [Fact]
    public async Task InsertsDuringTraversalFollowTheKeysetBoundaryWithoutDuplicates()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "history-inserts");
        SetBearer(client, authentication.AccessToken);
        var patientId = EntityId.From(authentication.Account.ProfileId);
        var initial = CreateHistoryGraph(
            patientId,
            eventCount: 6,
            projectIndexes: Enumerable.Range(0, 6).ToArray(),
            occurrenceMinuteOffset: 0);
        await SaveHistoryGraphAsync(initial);

        using var firstResponse = await client.GetAsync(
            Endpoint(authentication.Account.ProfileId) + "?pageSize=2");
        var first = Assert.IsType<HistoryPage>(
            await firstResponse.Content.ReadFromJsonAsync<HistoryPage>());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(first.NextCursor);

        var newer = CreateHistoryGraph(
            patientId,
            eventCount: 1,
            projectIndexes: [0],
            occurrenceMinuteOffset: 30);
        var older = CreateHistoryGraph(
            patientId,
            eventCount: 1,
            projectIndexes: [0],
            occurrenceMinuteOffset: -30);
        await SaveHistoryGraphAsync(newer);
        await SaveHistoryGraphAsync(older);

        var laterItems = new List<Guid>();
        var cursor = first.NextCursor;
        while (cursor is not null)
        {
            using var response = await client.GetAsync(
                Endpoint(authentication.Account.ProfileId) +
                $"?pageSize=2&cursor={cursor}");
            var page = Assert.IsType<HistoryPage>(
                await response.Content.ReadFromJsonAsync<HistoryPage>());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            laterItems.AddRange(page.Items.Select(item => item.EventId));
            cursor = page.NextCursor;
        }

        var allObserved = first.Items.Select(item => item.EventId)
            .Concat(laterItems)
            .ToArray();
        Assert.Equal(allObserved.Length, allObserved.Distinct().Count());
        Assert.DoesNotContain(newer.HistoryEvents[0].Id.Value, allObserved);
        Assert.Contains(older.HistoryEvents[0].Id.Value, laterItems);
        Assert.All(
            initial.HistoryEvents,
            historyEvent => Assert.Contains(historyEvent.Id.Value, allObserved));
    }

    private HistoryGraph CreateHistoryGraph(
        EntityId patientProfileId,
        int eventCount,
        IReadOnlyCollection<int> projectIndexes,
        bool deterministicEventIds = false,
        int occurrenceMinuteOffset = 0)
    {
        var questionnaire = QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create($"history-list-{Guid.NewGuid():N}"),
            DefinitionVersion.Create("test-version"),
            DefinitionHash.FromHash(new string('a', 64)),
            Now,
            Now);
        var ruleSet = ClinicalRuleSetVersion.ImportApproved(
            RuleSetCode.Create($"history-list-{Guid.NewGuid():N}"),
            DefinitionVersion.Create("test-version"),
            DefinitionHash.FromHash(new string('b', 64)),
            Now,
            Now);
        var sessions = new List<PreTriageSession>();
        var episodes = new List<PreTriageEpisode>();
        var historyEvents = new List<ClinicalHistoryEvent>();

        for (var index = 0; index < eventCount; index++)
        {
            var createdAt = Now.AddHours(-4).AddMinutes(index);
            var occurredAt = Now.AddHours(-2).AddMinutes(
                occurrenceMinuteOffset + (index == 6 ? 5 : index));
            var session = PreTriageSession.CreateForPatient(
                patientProfileId,
                questionnaire.Id,
                createdAt.AddDays(1),
                createdAt);
            var episode = PreTriageEpisode.CreateFrom(session, ruleSet.Id, occurredAt);
            sessions.Add(session);
            episodes.Add(episode);
            if (projectIndexes.Contains(index))
            {
                var eventId = deterministicEventIds
                    ? EntityId.From(Guid.Parse(
                        $"aaaaaaaa-aaaa-aaaa-aaaa-{index + 1:D12}"))
                    : (EntityId?)null;
                historyEvents.Add(ClinicalHistoryEvent.CreateCompletedPreTriage(
                    episode,
                    occurredAt.AddSeconds(5),
                    eventId));
            }
        }

        return new HistoryGraph(
            patientProfileId,
            questionnaire,
            ruleSet,
            sessions,
            episodes,
            historyEvents);
    }

    private async Task SaveHistoryGraphAsync(HistoryGraph graph)
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(graph.Questionnaire, graph.RuleSet);
        dbContext.AddRange(graph.Sessions);
        dbContext.AddRange(graph.Episodes);
        dbContext.AddRange(graph.HistoryEvents);
        await dbContext.SaveChangesAsync();
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

    private static string Endpoint(Guid patientId) =>
        $"/api/v1/patients/{patientId:D}/clinical-history";

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
        EntityId PatientProfileId,
        QuestionnaireDefinitionVersion Questionnaire,
        ClinicalRuleSetVersion RuleSet,
        IReadOnlyList<PreTriageSession> Sessions,
        IReadOnlyList<PreTriageEpisode> Episodes,
        IReadOnlyList<ClinicalHistoryEvent> HistoryEvents);

    private sealed record AuthenticationResult(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);

    private sealed record HistoryPage(
        IReadOnlyList<HistoryItem> Items,
        string? NextCursor);

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

    private sealed record ProblemResponse(
        int Status,
        string Title,
        string? Type,
        string? Detail,
        string? ErrorCode);
}
