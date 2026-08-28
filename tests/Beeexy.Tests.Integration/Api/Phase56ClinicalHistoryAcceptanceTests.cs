using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class Phase56ClinicalHistoryAcceptanceTests(
    PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task TwoManagers_RevocationOnlyRemovesRevokedManagerPhase5Authority()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var managerAClient = factory.CreateApiClient();
        using var managerBClient = factory.CreateApiClient();
        var managerA = await AuthenticateAsync(factory, managerAClient, "phase56-manager-a");
        var managerB = await AuthenticateAsync(factory, managerBClient, "phase56-manager-b");
        SetBearer(managerAClient, managerA.AccessToken);
        SetBearer(managerBClient, managerB.AccessToken);

        var now = DateTimeOffset.UtcNow;
        var patient = PatientProfile.CreateManaged(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            PatientName.Create("Phase"),
            PatientName.Create("Closure"),
            new DateOnly(2012, 1, 1),
            SexAssignedAtBirth.Female,
            UsState.Create("NY"),
            now);
        var relationshipA = CreateRelationship(managerA, patient, now);
        var relationshipB = CreateRelationship(managerB, patient, now.AddSeconds(1));
        await using (var seed = CreateDbContext())
        {
            seed.AddRange(patient, relationshipA, relationshipB);
            await seed.SaveChangesAsync();
        }

        var completed = await CompleteAuthenticatedAsync(
            managerAClient,
            patient.Id.Value);
        var sourceBefore = await LoadClinicalSourceSnapshotAsync(completed.EpisodeId);

        var listA = await GetHistoryAsync(managerAClient, patient.Id.Value);
        var eventItem = Assert.Single(listA.Items);
        var detailA = await GetDetailAsync(
            managerAClient,
            patient.Id.Value,
            eventItem.EventId);
        Assert.Equal(completed.EpisodeId, detailA.Source.Id);

        var amendmentA = await CreateAmendmentAsync(
            managerAClient,
            completed.EpisodeId,
            "Manager A traceable correction");
        Assert.Equal(managerA.Account.BeeexyId, amendmentA.Author.BeeexyId);

        var listB = await GetHistoryAsync(managerBClient, patient.Id.Value);
        Assert.Equal(eventItem.EventId, Assert.Single(listB.Items).EventId);
        var detailB = await GetDetailAsync(
            managerBClient,
            patient.Id.Value,
            eventItem.EventId);
        Assert.Equal(amendmentA.AmendmentId, Assert.Single(detailB.Amendments).AmendmentId);
        var amendmentB = await CreateAmendmentAsync(
            managerBClient,
            completed.EpisodeId,
            "Manager B independent correction");
        Assert.Equal(managerB.Account.BeeexyId, amendmentB.Author.BeeexyId);

        using (var revoke = await managerAClient.DeleteAsync(
            $"/api/v1/care-relationships/{relationshipA.Id.Value:D}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }

        using var revokedList = await managerAClient.GetAsync(HistoryEndpoint(patient.Id.Value));
        using var revokedDetail = await managerAClient.GetAsync(
            DetailEndpoint(patient.Id.Value, eventItem.EventId));
        using var revokedAmendment = await managerAClient.PostAsJsonAsync(
            AmendmentEndpoint(completed.EpisodeId),
            ValidAmendment("Manager A no longer has authority"));
        Assert.Equal(HttpStatusCode.NotFound, revokedList.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revokedDetail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revokedAmendment.StatusCode);

        var survivingList = await GetHistoryAsync(managerBClient, patient.Id.Value);
        Assert.Equal(eventItem.EventId, Assert.Single(survivingList.Items).EventId);
        var survivingDetail = await GetDetailAsync(
            managerBClient,
            patient.Id.Value,
            eventItem.EventId);
        Assert.Equal(
            [amendmentA.AmendmentId, amendmentB.AmendmentId],
            survivingDetail.Amendments.Select(item => item.AmendmentId));
        var laterAmendmentB = await CreateAmendmentAsync(
            managerBClient,
            completed.EpisodeId,
            "Manager B remains authorized");
        Assert.Equal(managerB.Account.BeeexyId, laterAmendmentB.Author.BeeexyId);

        Assert.Equal(
            sourceBefore,
            await LoadClinicalSourceSnapshotAsync(completed.EpisodeId));
        var completedSourceId = EntityId.From(completed.EpisodeId);
        await using var verify = CreateDbContext();
        Assert.Equal(1, await verify.ClinicalHistoryEvents.CountAsync(
            item => item.SourceId == completedSourceId));
        Assert.Equal(3, await verify.ClinicalAmendments.CountAsync(
            item => item.SourceId == completedSourceId));
        Assert.Equal(1, await verify.CareRelationships.CountAsync(item =>
            item.SubjectProfileId == patient.Id &&
            item.Status == CareRelationshipStatus.Active));
        Assert.Equal(1, await verify.CareRelationships.CountAsync(item =>
            item.SubjectProfileId == patient.Id &&
            item.Status == CareRelationshipStatus.Revoked));
    }

    [Fact]
    public async Task AnonymousCompletion_IsExcludedUntilClaimThenSupportsFullHistoryJourney()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var anonymousClient = factory.CreateApiClient();
        using var patientClient = factory.CreateApiClient();
        var patient = await AuthenticateAsync(factory, patientClient, "phase56-claim");
        SetBearer(patientClient, patient.AccessToken);

        var anonymous = await StartAsync(anonymousClient, patientId: null);
        await AnswerAsync(anonymousClient, anonymous, useCapability: true);
        var completed = await CompleteAsync(
            anonymousClient,
            anonymous,
            useCapability: true);

        var beforeClaim = await GetHistoryAsync(patientClient, patient.Account.ProfileId);
        Assert.Empty(beforeClaim.Items);
        var completedSourceId = EntityId.From(completed.EpisodeId);
        await using (var verifyUnclaimed = CreateDbContext())
        {
            Assert.False(await verifyUnclaimed.ClinicalHistoryEvents.AnyAsync(
                item => item.SourceId == completedSourceId));
        }

        var firstClaim = await ClaimAsync(patientClient, anonymous);
        var repeatedClaim = await ClaimAsync(patientClient, anonymous);
        Assert.Equal(firstClaim, repeatedClaim);
        Assert.Equal(patient.Account.ProfileId, firstClaim.PatientId);

        var sourceAfterClaim = await LoadClinicalSourceSnapshotAsync(completed.EpisodeId);
        var history = await GetHistoryAsync(patientClient, patient.Account.ProfileId);
        var eventItem = Assert.Single(history.Items);
        Assert.Equal(completed.EpisodeId, eventItem.Source.Id);
        var detail = await GetDetailAsync(
            patientClient,
            patient.Account.ProfileId,
            eventItem.EventId);
        Assert.Empty(detail.Amendments);

        var amendment = await CreateAmendmentAsync(
            patientClient,
            completed.EpisodeId,
            "Post-claim traceable correction");
        var amendedDetail = await GetDetailAsync(
            patientClient,
            patient.Account.ProfileId,
            eventItem.EventId);
        Assert.Equal(amendment.AmendmentId,
            Assert.Single(amendedDetail.Amendments).AmendmentId);
        Assert.Equal(
            sourceAfterClaim,
            await LoadClinicalSourceSnapshotAsync(completed.EpisodeId));

        await using var verify = CreateDbContext();
        Assert.Equal(1, await verify.PreTriageHistoryProjectionRecords.CountAsync(
            item => item.SourceEpisodeId == completedSourceId));
        Assert.Equal(1, await verify.ClinicalHistoryEvents.CountAsync(
            item => item.SourceId == completedSourceId));
        Assert.Equal(1, await verify.ClinicalAmendments.CountAsync(
            item => item.SourceId == completedSourceId));
    }

    private static CareRelationship CreateRelationship(
        AuthenticationResult manager,
        PatientProfile patient,
        DateTimeOffset now) =>
        CareRelationship.Create(
            EntityId.From(manager.Account.ProfileId),
            patient.Id,
            CareRelationshipType.Caregiver,
            EntityId.From(manager.Account.AccountId),
            AuthorizationAttestation.Create("phase-5.6-acceptance", now),
            now);

    private static async Task<CompletedJourney> CompleteAuthenticatedAsync(
        HttpClient client,
        Guid patientId)
    {
        var started = await StartAsync(client, patientId);
        await AnswerAsync(client, started, useCapability: false);
        return await CompleteAsync(client, started, useCapability: false);
    }

    private static async Task<StartedJourney> StartAsync(
        HttpClient client,
        Guid? patientId)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/pre-triage/sessions",
            new { pathway = "HEADACHE", patientId });
        var started = await response.Content.ReadFromJsonAsync<StartedJourney>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<StartedJourney>(started);
    }

    private static async Task AnswerAsync(
        HttpClient client,
        StartedJourney started,
        bool useCapability)
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
        AddCapability(request, started, useCapability);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var offerRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/educational-video-offer")
        {
            Content = JsonContent.Create(new { decision = "SKIP" })
        };
        AddCapability(offerRequest, started, useCapability);
        using var offerResponse = await client.SendAsync(offerRequest);
        Assert.Equal(HttpStatusCode.OK, offerResponse.StatusCode);
    }

    private static async Task<CompletedJourney> CompleteAsync(
        HttpClient client,
        StartedJourney started,
        bool useCapability)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/complete");
        AddCapability(request, started, useCapability);
        using var response = await client.SendAsync(request);
        var completed = await response.Content.ReadFromJsonAsync<CompletedJourney>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<CompletedJourney>(completed);
    }

    private static async Task<ClaimResponse> ClaimAsync(
        HttpClient client,
        StartedJourney started)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/claim");
        request.Headers.Add("X-Pre-Triage-Capability", started.AnonymousCapability);
        using var response = await client.SendAsync(request);
        var claim = await response.Content.ReadFromJsonAsync<ClaimResponse>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<ClaimResponse>(claim);
    }

    private static void AddCapability(
        HttpRequestMessage request,
        StartedJourney started,
        bool useCapability)
    {
        if (useCapability)
        {
            request.Headers.Add("X-Pre-Triage-Capability", started.AnonymousCapability);
        }
    }

    private static async Task<HistoryPage> GetHistoryAsync(
        HttpClient client,
        Guid patientId)
    {
        using var response = await client.GetAsync(HistoryEndpoint(patientId));
        var page = await response.Content.ReadFromJsonAsync<HistoryPage>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<HistoryPage>(page);
    }

    private static async Task<HistoryDetail> GetDetailAsync(
        HttpClient client,
        Guid patientId,
        Guid eventId)
    {
        using var response = await client.GetAsync(DetailEndpoint(patientId, eventId));
        var detail = await response.Content.ReadFromJsonAsync<HistoryDetail>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<HistoryDetail>(detail);
    }

    private static async Task<AmendmentResponse> CreateAmendmentAsync(
        HttpClient client,
        Guid episodeId,
        string reason)
    {
        using var response = await client.PostAsJsonAsync(
            AmendmentEndpoint(episodeId),
            ValidAmendment(reason));
        var amendment = await response.Content.ReadFromJsonAsync<AmendmentResponse>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<AmendmentResponse>(amendment);
    }

    private async Task<ClinicalSourceSnapshot> LoadClinicalSourceSnapshotAsync(Guid episodeId)
    {
        await using var dbContext = CreateDbContext();
        var sourceId = EntityId.From(episodeId);
        var episode = await dbContext.PreTriageEpisodes
            .AsNoTracking()
            .Include(item => item.Answers)
            .Include(item => item.ReportedSymptoms)
            .AsSplitQuery()
            .SingleAsync(item => item.Id == sourceId);
        var assessment = await dbContext.ClinicalAssessments
            .AsNoTracking()
            .Include(item => item.Findings)
            .SingleAsync(item => item.EpisodeId == sourceId);
        var historyEvent = await dbContext.ClinicalHistoryEvents
            .AsNoTracking()
            .SingleAsync(item => item.SourceId == sourceId);
        return new ClinicalSourceSnapshot(
            $"{episode.Id}|{episode.SourceSessionId}|{episode.PatientProfileId}|" +
            $"{episode.QuestionnaireVersionId}|{episode.ClinicalRuleSetVersionId}|" +
            $"{episode.CompletedAt:O}|{episode.ClaimedAt:O}",
            string.Join(';', episode.Answers.OrderBy(item => item.Id.Value).Select(item =>
                $"{item.Id}|{item.AnswerJson}|{item.RecordedAt:O}")),
            string.Join(';', episode.ReportedSymptoms.OrderBy(item => item.Id.Value).Select(
                item => $"{item.Id}|{item.OriginalText.Value}|{item.ReportedAt:O}")),
            $"{assessment.Id}|{assessment.ClinicalRuleSetVersionId}|" +
            $"{assessment.UrgencyCode?.Value}|{assessment.CreatedAt:O}|" +
            $"{assessment.Findings.Count}",
            $"{historyEvent.Id}|{historyEvent.PatientProfileId}|{historyEvent.SourceId}|" +
            $"{historyEvent.SourceQuestionnaireVersionId}|" +
            $"{historyEvent.SourceClinicalRuleSetVersionId}|{historyEvent.OccurredAt:O}|" +
            $"{historyEvent.RecordedAt:O}");
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

    private static object ValidAmendment(string reason) =>
        new { idempotencyKey = Guid.NewGuid(), reason };

    private static string HistoryEndpoint(Guid patientId) =>
        $"/api/v1/patients/{patientId:D}/clinical-history";

    private static string DetailEndpoint(Guid patientId, Guid eventId) =>
        $"{HistoryEndpoint(patientId)}/{eventId:D}";

    private static string AmendmentEndpoint(Guid episodeId) =>
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

    private sealed record AuthenticationResult(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);

    private sealed record StartedJourney(Guid SessionId, string? AnonymousCapability);

    private sealed record CompletedJourney(Guid EpisodeId, DateTimeOffset CompletedAt);

    private sealed record ClaimResponse(
        Guid SessionId,
        Guid EpisodeId,
        Guid PatientId,
        DateTimeOffset ClaimedAt);

    private sealed record HistoryPage(
        IReadOnlyList<HistoryItem> Items,
        string? NextCursor);

    private sealed record HistoryItem(Guid EventId, HistorySource Source);

    private sealed record HistorySource(
        string Type,
        Guid Id,
        Guid QuestionnaireVersionId,
        Guid ClinicalRuleSetVersionId);

    private sealed record HistoryDetail(
        HistorySource Source,
        IReadOnlyList<AmendmentResponse> Amendments);

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

    private sealed record ClinicalSourceSnapshot(
        string Episode,
        string Answers,
        string Symptoms,
        string Assessment,
        string HistoryEvent);
}
