using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Integration.Support;

namespace Beeexy.Tests.Integration.Api;

public sealed partial class SymptomDiaryContentEndpointTests
{
    [Theory]
    [InlineData("HEADACHE")]
    [InlineData("ABDOMINAL_PAIN")]
    [InlineData("FEVER")]
    [InlineData("CHEST_PAIN")]
    [Trait("Category", "Phase97")]
    [Trait("Category", "Phase97Acceptance")]
    public async Task ReviewedContentToVoluntaryEntryToFrozenHistoryIsExactNeutralAndIsolated(
        string pathwayValue)
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(
            factory,
            client,
            $"phase97-{pathwayValue.ToLowerInvariant()}");
        SetBearer(client, authentication.AccessToken);
        var pathway = ClinicalPathwayCode.Create(pathwayValue);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            pathway);
        var definition = AndreaSymptomDiaryPackages.Create(pathway);
        var answers = AllAnswers(definition);
        var before = await SideEffectSnapshotAsync(episode.Id);

        using var content = await client.GetAsync(Endpoint(episode.Id.Value));
        using var contentJson = JsonDocument.Parse(
            await content.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        await AssertExactContentAsync(contentJson.RootElement, episode.Id, pathway);
        AssertNoInterpretedContractFields(contentJson.RootElement);
        Assert.Equal(before, await SideEffectSnapshotAsync(episode.Id));

        var packageVersionId = contentJson.RootElement
            .GetProperty("packageVersionId").GetGuid();
        using var creation = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(packageVersionId, Guid.NewGuid(), answers));
        using var creationJson = JsonDocument.Parse(
            await creation.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        AssertNoInterpretedContractFields(creationJson.RootElement);

        using var history = await client.GetAsync(CheckInEndpoint(episode.Id.Value));
        using var historyJson = JsonDocument.Parse(
            await history.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var historical = Assert.Single(
            historyJson.RootElement.GetProperty("items").EnumerateArray());
        AssertNoInterpretedContractFields(historical);
        Assert.Equal(packageVersionId,
            historical.GetProperty("packageVersionId").GetGuid());
        Assert.Equal(definition.ExpectedContentHash!.Value,
            historical.GetProperty("contentHash").GetString());
        var returnedAnswers = historical.GetProperty("answers").EnumerateArray().ToArray();
        Assert.Equal(answers.Length, returnedAnswers.Length);
        for (var index = 0; index < answers.Length; index++)
        {
            Assert.Equal(
                answers[index]["questionCode"]!.GetValue<string>(),
                returnedAnswers[index].GetProperty("questionCode").GetString());
            Assert.True(JsonNode.DeepEquals(
                answers[index]["value"],
                JsonNode.Parse(returnedAnswers[index].GetProperty("value").GetRawText())));
        }

        Assert.Equal(before with
        {
            CheckIns = before.CheckIns + 1,
            Answers = before.Answers + answers.Length
        }, await SideEffectSnapshotAsync(episode.Id));
    }

    [Fact]
    [Trait("Category", "Phase97")]
    [Trait("Category", "Phase97Acceptance")]
    public async Task DiaryPayloadsAndReviewedMedicalTextNeverEnterTechnicalLogs()
    {
        await ImportApprovedContentAsync();
        using var logs = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(ConnectionString, loggerProvider: logs);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase97-privacy");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            ClinicalPathways.Headache);
        var definition = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        var answers = AllAnswers(definition);
        const string privateText = "PRIVATE-SYMPTOM-VALUE-9-7";
        var freeTextIndex = Array.FindIndex(answers, answer =>
            definition.Questions.Single(question =>
                question.Code.Value == answer["questionCode"]!.GetValue<string>())
                .Options.Count == 0);
        Assert.True(freeTextIndex >= 0);
        answers[freeTextIndex]["value"] = privateText;

        using var create = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(await PackageIdAsync(ClinicalPathways.Headache), Guid.NewGuid(), answers));
        using var history = await client.GetAsync(CheckInEndpoint(episode.Id.Value));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);

        var renderedLogs = string.Join('\n', logs.Messages);
        Assert.DoesNotContain(privateText, renderedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(
            definition.Questions[0].PromptText,
            renderedLogs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            definition.WarningSigns[0].DisplayText,
            renderedLogs,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Phase97")]
    [Trait("Category", "Phase97Acceptance")]
    public async Task MissingEndpointAuthorizationCasesAreConcealedAcrossAllThreeOperations()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var subjectClient = factory.CreateApiClient();
        using var managerClient = factory.CreateApiClient();
        using var unrelatedClient = factory.CreateApiClient();
        var subject = await AuthenticateAsync(factory, subjectClient, "phase97-subject");
        var manager = await AuthenticateAsync(factory, managerClient, "phase97-manager");
        var unrelated = await AuthenticateAsync(factory, unrelatedClient, "phase97-unrelated");
        SetBearer(subjectClient, subject.AccessToken);
        SetBearer(managerClient, manager.AccessToken);
        SetBearer(unrelatedClient, unrelated.AccessToken);
        var packageId = await PackageIdAsync(ClinicalPathways.Headache);

        var triage = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var incomplete = PreTriageSession.CreateForPatient(
            EntityId.From(subject.Account.ProfileId),
            triage.Questionnaire.Id,
            Now.AddDays(1),
            Now);
        var anonymous = PreTriageSession.CreateAnonymous(
            triage.Questionnaire.Id,
            AnonymousCapabilityHash.FromHash(Guid.NewGuid().ToString("N")),
            Now.AddDays(1),
            Now);
        var unclaimed = PreTriageEpisode.CreateFrom(
            anonymous,
            triage.RuleSet.Id,
            Now.AddMinutes(1),
            Now.AddHours(24));
        var reverseRelationship = CareRelationship.Create(
            EntityId.From(manager.Account.ProfileId),
            EntityId.From(subject.Account.ProfileId),
            CareRelationshipType.Caregiver,
            EntityId.From(manager.Account.AccountId),
            AuthorizationAttestation.Create("phase-9.7-reverse", Now),
            Now);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(incomplete, anonymous, unclaimed, reverseRelationship);
            await dbContext.SaveChangesAsync();
        }

        var managerEpisode = await SeedSupportedEpisodeAsync(
            EntityId.From(manager.Account.ProfileId),
            ClinicalPathways.Headache);
        await AssertConcealedForAllOperationsAsync(
            subjectClient,
            managerEpisode.Id.Value,
            packageId);
        await AssertConcealedForAllOperationsAsync(
            unrelatedClient,
            managerEpisode.Id.Value,
            packageId);
        await AssertConcealedForAllOperationsAsync(
            subjectClient,
            unclaimed.Id.Value,
            packageId);
        await AssertConcealedForAllOperationsAsync(
            subjectClient,
            incomplete.Id.Value,
            packageId);
        await AssertConcealedForAllOperationsAsync(
            subjectClient,
            Guid.NewGuid(),
            packageId);

        using var malformedContent = await subjectClient.GetAsync(
            "/api/v1/pre-triage/episodes/not-a-guid/symptom-diary-content");
        using var malformedCreation = await subjectClient.PostAsJsonAsync(
            "/api/v1/pre-triage/episodes/not-a-guid/check-ins",
            Request(packageId, Guid.NewGuid(), []));
        using var malformedHistory = await subjectClient.GetAsync(
            "/api/v1/pre-triage/episodes/not-a-guid/check-ins");
        Assert.Equal(HttpStatusCode.NotFound, malformedContent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformedCreation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformedHistory.StatusCode);
    }

    [Fact]
    [Trait("Category", "Phase97")]
    [Trait("Category", "Phase97Acceptance")]
    public async Task OpenApiContainsExactlyTheThreeBearerSecuredPhase9Operations()
    {
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(60, paths.EnumerateObject().Count());

        var operations = paths.EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Name is "get" or "post" or "put" or "patch" or "delete")
                .Select(operation => (Path: path.Name, Verb: operation.Name, Value: operation.Value)))
            .Where(operation =>
                operation.Path.Contains("symptom-diary", StringComparison.OrdinalIgnoreCase) ||
                operation.Path.EndsWith("/check-ins", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            [
                ("/api/v1/pre-triage/episodes/{episodeId}/check-ins", "get"),
                ("/api/v1/pre-triage/episodes/{episodeId}/check-ins", "post"),
                ("/api/v1/pre-triage/episodes/{episodeId}/symptom-diary-content", "get")
            ],
            operations.Select(operation => (operation.Path, operation.Verb))
                .OrderBy(operation => operation.Path)
                .ThenBy(operation => operation.Verb));
        Assert.All(operations, operation =>
            Assert.Contains(operation.Value.GetProperty("security").EnumerateArray(),
                security => security.TryGetProperty("Bearer", out _)));
    }

    private async Task AssertConcealedForAllOperationsAsync(
        HttpClient client,
        Guid episodeId,
        Guid packageId)
    {
        using var content = await client.GetAsync(Endpoint(episodeId));
        using var creation = await client.PostAsJsonAsync(
            CheckInEndpoint(episodeId),
            Request(packageId, Guid.NewGuid(), []));
        using var history = await client.GetAsync(CheckInEndpoint(episodeId));

        Assert.Equal(HttpStatusCode.NotFound, content.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, creation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
        var contentProblem = await ProblemAsync(content);
        var creationProblem = await ProblemAsync(creation);
        var historyProblem = await ProblemAsync(history);
        Assert.Equal(contentProblem, creationProblem);
        Assert.Equal(creationProblem, historyProblem);
    }
}
