using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beeexy.Application.Care;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beeexy.Tests.Integration.Api;

public sealed partial class SymptomDiaryContentEndpointTests
{
    [Fact]
    [Trait("Category", "Phase95")]
    public async Task CheckInAuthenticationAndEpisodeConcealmentAreEnforced()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var ownerClient = factory.CreateApiClient();
        using var otherClient = factory.CreateApiClient();
        using var unauthenticated = await ownerClient.PostAsJsonAsync(
            CheckInEndpoint(Guid.NewGuid()),
            Request(Guid.NewGuid(), Guid.NewGuid(), []));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var owner = await AuthenticateAsync(factory, ownerClient, "phase95-owner");
        var other = await AuthenticateAsync(factory, otherClient, "phase95-other");
        SetBearer(ownerClient, owner.AccessToken);
        SetBearer(otherClient, other.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(owner.Account.ProfileId),
            ClinicalPathways.Headache);
        var packageId = await PackageIdAsync(ClinicalPathways.Headache);

        using var unknown = await ownerClient.PostAsJsonAsync(
            CheckInEndpoint(Guid.NewGuid()),
            Request(packageId, Guid.NewGuid(), []));
        using var crossAccount = await otherClient.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(packageId, Guid.NewGuid(), []));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossAccount.StatusCode);
        var concealedProblem = await ProblemAsync(unknown);
        Assert.Equal(concealedProblem, await ProblemAsync(crossAccount));

        var triage = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var incomplete = PreTriageSession.CreateForPatient(
            EntityId.From(owner.Account.ProfileId),
            triage.Questionnaire.Id,
            Now.AddDays(1),
            Now);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Add(incomplete);
            await dbContext.SaveChangesAsync();
        }

        using var incompleteResponse = await ownerClient.PostAsJsonAsync(
            CheckInEndpoint(incomplete.Id.Value),
            Request(packageId, Guid.NewGuid(), []));
        Assert.Equal(HttpStatusCode.NotFound, incompleteResponse.StatusCode);
        Assert.Equal(concealedProblem, await ProblemAsync(incompleteResponse));
    }

    [Fact]
    [Trait("Category", "Phase95")]
    public async Task RevokedManagerCannotCreateCheckIn()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var manager = await AuthenticateAsync(factory, client, "phase95-manager");
        SetBearer(client, manager.AccessToken);
        var patient = PatientProfile.CreateManaged(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            PatientName.Create("Ana"),
            PatientName.Create("Rios"),
            new DateOnly(2010, 2, 3),
            SexAssignedAtBirth.Female,
            UsState.Create("NY"),
            Now);
        var relationship = CareRelationship.Create(
            EntityId.From(manager.Account.ProfileId),
            patient.Id,
            CareRelationshipType.Caregiver,
            EntityId.From(manager.Account.AccountId),
            AuthorizationAttestation.Create("phase-9.5-check-in", Now),
            Now);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(patient, relationship);
            await dbContext.SaveChangesAsync();
        }

        var episode = await SeedSupportedEpisodeAsync(patient.Id, ClinicalPathways.Headache);
        var packageId = await PackageIdAsync(ClinicalPathways.Headache);
        using var authorized = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(packageId, Guid.NewGuid(), []));
        Assert.Equal(HttpStatusCode.Created, authorized.StatusCode);
        await using (var dbContext = CreateDbContext())
        {
            var persisted = await dbContext.CareRelationships.SingleAsync(
                value => value.Id == relationship.Id);
            persisted.Revoke(EntityId.From(manager.Account.AccountId), Now.AddMinutes(1));
            await dbContext.SaveChangesAsync();
        }

        using var response = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(packageId, Guid.NewGuid(), []));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var verify = CreateDbContext();
        Assert.Single(await verify.SymptomCheckIns.ToArrayAsync());
    }

    [Theory]
    [InlineData("HEADACHE")]
    [InlineData("ABDOMINAL_PAIN")]
    [InlineData("FEVER")]
    [InlineData("CHEST_PAIN")]
    [Trait("Category", "Phase95")]
    public async Task ValidExactAndreaEntryPersistsAtomicallyAndRoundTrips(
        string pathwayValue)
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(
            factory,
            client,
            $"phase95-{pathwayValue.ToLowerInvariant()}");
        SetBearer(client, authentication.AccessToken);
        var pathway = ClinicalPathwayCode.Create(pathwayValue);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            pathway);
        var definition = AndreaSymptomDiaryPackages.Create(pathway);
        var packageId = await PackageIdAsync(pathway);
        var answers = AllAnswers(definition);
        var before = await SideEffectSnapshotAsync(episode.Id);

        using var response = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(packageId, Guid.NewGuid(), answers));
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal(episode.Id.Value, root.GetProperty("episodeId").GetGuid());
        Assert.Equal(packageId, root.GetProperty("packageVersionId").GetGuid());
        Assert.Equal(pathway.Value, root.GetProperty("pathway").GetString());
        Assert.True(root.GetProperty("createdAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
        AssertNoInterpretedContractFields(root);
        var returnedAnswers = root.GetProperty("answers").EnumerateArray().ToArray();
        Assert.Equal(definition.Questions.Count, returnedAnswers.Length);
        Assert.Equal(
            definition.Questions.Select(value => value.Code.Value),
            returnedAnswers.Select(value => value.GetProperty("questionCode").GetString()));

        await using var dbContext = CreateDbContext();
        var checkIn = await dbContext.SymptomCheckIns.AsNoTracking().SingleAsync(
            value => value.EpisodeId == episode.Id);
        var persistedAnswers = await dbContext.SymptomCheckInAnswers.AsNoTracking()
            .Where(value => value.CheckInId == checkIn.Id)
            .OrderBy(value => value.SourceOrder)
            .ToArrayAsync();
        Assert.Equal(packageId, checkIn.PackageVersionId.Value);
        Assert.Equal(EntityId.From(authentication.Account.AccountId),
            checkIn.SubmittingAccountId);
        Assert.Equal(definition.Questions.Count, persistedAnswers.Length);
        Assert.All(persistedAnswers, value => Assert.Equal(checkIn.PackageVersionId,
            value.PackageVersionId));
        for (var index = 0; index < persistedAnswers.Length; index++)
        {
            Assert.True(JsonNode.DeepEquals(
                answers[index]["value"],
                JsonNode.Parse(persistedAnswers[index].SubmittedValueJson)));
        }

        var after = await SideEffectSnapshotAsync(episode.Id);
        Assert.Equal(before with
        {
            CheckIns = before.CheckIns + 1,
            Answers = before.Answers + definition.Questions.Count
        }, after);
    }

    [Fact]
    [Trait("Category", "Phase95")]
    public async Task EmptyOptionalSubmissionCreatesNoFabricatedAnswerRows()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase95-optional");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            ClinicalPathways.Headache);

        using var response = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(await PackageIdAsync(ClinicalPathways.Headache), Guid.NewGuid(), []));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var dbContext = CreateDbContext();
        Assert.Single(await dbContext.SymptomCheckIns.ToArrayAsync());
        Assert.Empty(await dbContext.SymptomCheckInAnswers.ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "Phase95")]
    public async Task OtherWrongPathAndInvalidAnswersFailWithZeroRows()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase95-invalid");
        SetBearer(client, authentication.AccessToken);
        var patientId = EntityId.From(authentication.Account.ProfileId);
        var headache = await SeedSupportedEpisodeAsync(patientId, ClinicalPathways.Headache);
        var other = await SeedSupportedEpisodeAsync(patientId, ClinicalPathways.OtherSymptoms);
        var headachePackage = await PackageIdAsync(ClinicalPathways.Headache);
        var feverPackage = await PackageIdAsync(ClinicalPathways.Fever);
        var definition = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        var question = definition.Questions[0];
        var inactive = ReidentifiedPackage(
            ClinicalPathways.RespiratorySymptoms,
            "phase95-inactive",
            ClinicalContentStatus.MedicalTeamApproved,
            AndreaSymptomDiaryPackages.ApprovalEffectiveAt,
            activatedAt: null);
        await ImportPackageAsync(inactive);
        var inactiveEpisode = await SeedCustomPathwayEpisodeAsync(
            patientId,
            ClinicalPathways.RespiratorySymptoms,
            "phase95-inactive");
        Guid inactivePackage;
        await using (var inactiveContext = CreateDbContext())
        {
            inactivePackage = await inactiveContext.SymptomDiaryPackageVersions
                .Where(value => value.PackageCode == inactive.PackageCode)
                .Select(value => value.Id.Value)
                .SingleAsync();
        }
        var unapproved = ReidentifiedPackage(
            ClinicalPathways.BackPain,
            "phase95-unapproved",
            new ClinicalContentStatus(
                ClinicalContentSource.MedicalTeamProvided,
                ClinicalReviewStatus.Provisional,
                ClinicalApprovalStatus.PendingFormalReview),
            approvedAt: null,
            activatedAt: null);
        await ImportPackageAsync(unapproved);
        var unapprovedEpisode = await SeedCustomPathwayEpisodeAsync(
            patientId,
            ClinicalPathways.BackPain,
            "phase95-unapproved");
        Guid unapprovedPackage;
        await using (var unapprovedContext = CreateDbContext())
        {
            unapprovedPackage = await unapprovedContext.SymptomDiaryPackageVersions
                .Where(value => value.PackageCode == unapproved.PackageCode)
                .Select(value => value.Id.Value)
                .SingleAsync();
        }

        var attempts = new[]
        {
            (other.Id.Value, headachePackage,
                Request(headachePackage, Guid.NewGuid(), [])),
            (headache.Id.Value, feverPackage,
                Request(feverPackage, Guid.NewGuid(), [])),
            (inactiveEpisode.Id.Value, inactivePackage,
                Request(inactivePackage, Guid.NewGuid(), [])),
            (unapprovedEpisode.Id.Value, unapprovedPackage,
                Request(unapprovedPackage, Guid.NewGuid(), [])),
            (headache.Id.Value, headachePackage,
                Request(headachePackage, Guid.NewGuid(),
                    [Answer("unknown-question", JsonValue.Create("x")!)])),
            (headache.Id.Value, headachePackage,
                Request(headachePackage, Guid.NewGuid(),
                    [Answer(question.Code.Value, JsonValue.Create(42)!)])),
            (headache.Id.Value, headachePackage,
                Request(headachePackage, Guid.NewGuid(),
                    [Answer(question.Code.Value, ValidValue(question)),
                     Answer(question.Code.Value, ValidValue(question))]))
        };

        foreach (var attempt in attempts)
        {
            using var response = await client.PostAsJsonAsync(
                CheckInEndpoint(attempt.Item1),
                attempt.Item3);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }

        await using var dbContext = CreateDbContext();
        Assert.Empty(await dbContext.SymptomCheckIns.ToArrayAsync());
        Assert.Empty(await dbContext.SymptomCheckInAnswers.ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "Phase95")]
    public async Task CorruptExactPackageFailsClosedWithoutInternalDetailsOrRows()
    {
        await ImportApprovedContentAsync();
        var throwingProvider = new ThrowingContentProvider();
        using var factory = new BeeexyApiFactory(
            ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<ISymptomDiaryContentProvider>();
                services.AddSingleton<ISymptomDiaryContentProvider>(throwingProvider);
            });
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase95-corrupt");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            ClinicalPathways.Headache);

        using var response = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(await PackageIdAsync(ClinicalPathways.Headache), Guid.NewGuid(), []));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("internal-package-secret", raw, StringComparison.Ordinal);
        Assert.Equal("symptom_diary.content_unavailable",
            JsonSerializer.Deserialize<ProblemResponse>(raw,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!.ErrorCode);
        await using var dbContext = CreateDbContext();
        Assert.Empty(await dbContext.SymptomCheckIns.ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "Phase95")]
    public async Task MalformedJsonUnsupportedFieldsAndQuerySelectorsAreRejected()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase95-contract");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            ClinicalPathways.Headache);
        var packageId = await PackageIdAsync(ClinicalPathways.Headache);

        using var malformedContent = new StringContent(
            "{\"packageVersionId\":",
            System.Text.Encoding.UTF8,
            "application/json");
        using var malformed = await client.PostAsync(
            CheckInEndpoint(episode.Id.Value),
            malformedContent);
        var unsupportedRequest = Request(packageId, Guid.NewGuid(), []);
        unsupportedRequest["patientId"] = Guid.NewGuid();
        using var unsupported = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            unsupportedRequest);
        using var query = await client.PostAsJsonAsync(
            $"{CheckInEndpoint(episode.Id.Value)}?pathway=FEVER",
            Request(packageId, Guid.NewGuid(), []));

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsupported.StatusCode);
        Assert.Equal("symptom_diary.unsupported_fields",
            (await ProblemAsync(unsupported)).ErrorCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, query.StatusCode);
        Assert.Equal("symptom_diary.unsupported_query", (await ProblemAsync(query)).ErrorCode);
        await using var dbContext = CreateDbContext();
        Assert.Empty(await dbContext.SymptomCheckIns.ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "Phase95")]
    public async Task SequentialAndConcurrentIdenticalRetriesCreateExactlyOneEntry()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        using var concurrentClient = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase95-retry");
        SetBearer(client, authentication.AccessToken);
        SetBearer(concurrentClient, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            ClinicalPathways.Fever);
        var packageId = await PackageIdAsync(ClinicalPathways.Fever);
        var key = Guid.NewGuid();
        var request = Request(
            packageId,
            key,
            AllAnswers(AndreaSymptomDiaryPackages.Create(ClinicalPathways.Fever)));

        using var first = await client.PostAsJsonAsync(CheckInEndpoint(episode.Id.Value), request);
        using var replay = await client.PostAsJsonAsync(CheckInEndpoint(episode.Id.Value), request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(
            JsonNode.Parse(await first.Content.ReadAsStringAsync())!["checkInId"]!.GetValue<Guid>(),
            JsonNode.Parse(await replay.Content.ReadAsStringAsync())!["checkInId"]!.GetValue<Guid>());

        var concurrentKey = Guid.NewGuid();
        var concurrentRequest = Request(
            packageId,
            concurrentKey,
            AllAnswers(AndreaSymptomDiaryPackages.Create(ClinicalPathways.Fever)));
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(CheckInEndpoint(episode.Id.Value), concurrentRequest),
            concurrentClient.PostAsJsonAsync(CheckInEndpoint(episode.Id.Value), concurrentRequest));
        using (responses[0])
        using (responses[1])
        {
            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.Created],
                responses.Select(value => value.StatusCode).OrderBy(value => value));
        }

        await using var dbContext = CreateDbContext();
        Assert.Equal(2, await dbContext.SymptomCheckIns.CountAsync());
    }

    [Fact]
    [Trait("Category", "Phase95")]
    public async Task ConflictingRetryReturns409AndPreservesOriginalEntry()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase95-conflict");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            ClinicalPathways.Headache);
        var packageId = await PackageIdAsync(ClinicalPathways.Headache);
        var key = Guid.NewGuid();
        var definition = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        var firstAnswer = Answer(
            definition.Questions[0].Code.Value,
            ValidValue(definition.Questions[0]));
        var changedAnswer = Answer(
            definition.Questions[1].Code.Value,
            ValidValue(definition.Questions[1]));

        using var first = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(packageId, key, [firstAnswer]));
        using var conflict = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(packageId, key, [changedAnswer]));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("symptom_diary.idempotency_conflict",
            (await ProblemAsync(conflict)).ErrorCode);
        await using var dbContext = CreateDbContext();
        Assert.Single(await dbContext.SymptomCheckIns.ToArrayAsync());
        Assert.Single(await dbContext.SymptomCheckInAnswers.ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "Phase95")]
    public async Task ExactOlderApprovedActivatedPackageCanBeSubmittedWithoutSubstitution()
    {
        await ImportApprovedContentAsync();
        var originalId = await PackageIdAsync(ClinicalPathways.Headache);
        var later = ReidentifiedPackage(
            ClinicalPathways.Headache,
            "later-active",
            ClinicalContentStatus.MedicalTeamApproved,
            AndreaSymptomDiaryPackages.ApprovalEffectiveAt.AddDays(1),
            AndreaSymptomDiaryPackages.ApprovalEffectiveAt.AddDays(1));
        await ImportPackageAsync(later);
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase95-historical");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            ClinicalPathways.Headache);

        using var response = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(originalId, Guid.NewGuid(), []));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(originalId, document.RootElement.GetProperty("packageVersionId").GetGuid());
        await using var dbContext = CreateDbContext();
        Assert.Equal(originalId,
            (await dbContext.SymptomCheckIns.SingleAsync()).PackageVersionId.Value);
    }

    [Fact]
    [Trait("Category", "Phase95")]
    public async Task CheckInAndAnswersRemainImmutableAndPhase94ContentRemainsCorrect()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase95-immutable");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            ClinicalPathways.ChestPain);
        var packageId = await PackageIdAsync(ClinicalPathways.ChestPain);
        var definition = AndreaSymptomDiaryPackages.Create(ClinicalPathways.ChestPain);
        var answer = Answer(
            definition.Questions[0].Code.Value,
            ValidValue(definition.Questions[0]));
        using var created = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(packageId, Guid.NewGuid(), [answer]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        await using (var dbContext = CreateDbContext())
        {
            var checkIn = await dbContext.SymptomCheckIns.SingleAsync();
            dbContext.SymptomCheckIns.Remove(checkIn);
            await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        }

        await using (var dbContext = CreateDbContext())
        {
            var persistedAnswer = await dbContext.SymptomCheckInAnswers.SingleAsync();
            dbContext.SymptomCheckInAnswers.Remove(persistedAnswer);
            await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        }

        using var content = await client.GetAsync(Endpoint(episode.Id.Value));
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        using var document = JsonDocument.Parse(await content.Content.ReadAsStringAsync());
        Assert.Equal(packageId, document.RootElement.GetProperty("packageVersionId").GetGuid());
    }

    [Fact]
    [Trait("Category", "Phase95")]
    public async Task OpenApiRetainsBearerProtectedPostAlongsideHistoryGet()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var checkIns = paths.GetProperty(
            "/api/v1/pre-triage/episodes/{episodeId}/check-ins");
        var operation = checkIns.GetProperty("post");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(60, paths.EnumerateObject().Count());
        Assert.True(checkIns.TryGetProperty("get", out _));
        Assert.Contains(operation.GetProperty("security").EnumerateArray(), value =>
            value.TryGetProperty("Bearer", out _));
        Assert.Equal(
            ["200", "201", "400", "401", "404", "409", "422", "500"],
            operation.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name).OrderBy(value => value));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("RecordSymptomCheckInRequest", out _));
        Assert.True(schemas.TryGetProperty("SymptomCheckInResponse", out _));
    }

    private async Task<Guid> PackageIdAsync(ClinicalPathwayCode pathway)
    {
        var definition = AndreaSymptomDiaryPackages.Create(pathway);
        await using var dbContext = CreateDbContext();
        return await dbContext.SymptomDiaryPackageVersions
            .Where(value => value.PackageCode == definition.PackageCode &&
                value.PackageVersion == definition.PackageVersion)
            .Select(value => value.Id.Value)
            .SingleAsync();
    }

    private async Task<CheckInSideEffectSnapshot> SideEffectSnapshotAsync(EntityId episodeId)
    {
        await using var dbContext = CreateDbContext();
        var episode = await dbContext.PreTriageEpisodes.AsNoTracking()
            .SingleAsync(value => value.Id == episodeId);
        return new CheckInSideEffectSnapshot(
            $"{episode.Id}|{episode.PatientProfileId}|{episode.QuestionnaireVersionId}|" +
            $"{episode.ClinicalRuleSetVersionId}|{episode.CompletedAt:O}|{episode.ClaimedAt:O}",
            await dbContext.SymptomCheckIns.CountAsync(),
            await dbContext.SymptomCheckInAnswers.CountAsync(),
            await dbContext.ClinicalHistoryEvents.CountAsync(),
            await dbContext.ClinicalAssessments.CountAsync(),
            await dbContext.FhirExports.CountAsync(),
            await dbContext.AiAnalysisRequests.CountAsync());
    }

    private static JsonObject Request(
        Guid packageVersionId,
        Guid idempotencyKey,
        IReadOnlyList<JsonObject> answers) => new()
        {
            ["packageVersionId"] = packageVersionId,
            ["idempotencyKey"] = idempotencyKey,
            ["answers"] = new JsonArray(answers.Select(value => value.DeepClone()).ToArray())
        };

    private static JsonObject Answer(string questionCode, JsonNode value) => new()
    {
        ["questionCode"] = questionCode,
        ["value"] = value.DeepClone()
    };

    private static JsonObject[] AllAnswers(SymptomDiaryPackageDefinition definition) =>
        definition.Questions.Select(question =>
            Answer(question.Code.Value, ValidValue(question))).ToArray();

    private static JsonNode ValidValue(SymptomDiaryQuestionDefinition question)
    {
        using var schema = JsonDocument.Parse(question.AnswerSchemaJson);
        return schema.RootElement.GetProperty("type").GetString() switch
        {
            "array" => new JsonArray(question.Options.Take(2)
                .Select(value => JsonValue.Create(value.Value)).ToArray()),
            "string" when question.Options.Count > 0 =>
                JsonValue.Create(question.Options[0].Value)!,
            "string" => JsonValue.Create("Texto exacto ≥ 38.0–38.9°C")!,
            _ => throw new InvalidOperationException()
        };
    }

    private static string CheckInEndpoint(Guid episodeId) =>
        $"/api/v1/pre-triage/episodes/{episodeId:D}/check-ins";

    private sealed record CheckInSideEffectSnapshot(
        string Episode,
        int CheckIns,
        int Answers,
        int HistoryEvents,
        int Assessments,
        int FhirExports,
        int AiAnalysisRequests);
}
