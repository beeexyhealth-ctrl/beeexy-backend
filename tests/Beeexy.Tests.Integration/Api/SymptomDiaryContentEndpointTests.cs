using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beeexy.Application.Care;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase94")]
public sealed partial class SymptomDiaryContentEndpointTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 14, 0, 0, TimeSpan.Zero);

    private readonly string databaseName = $"phase94_{Guid.NewGuid():N}";

    private string ConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
            {
                Database = databaseName
            };
            return builder.ConnectionString;
        }
    }

    [Fact]
    public async Task AuthenticationUnknownIncompleteAndUnclaimedEpisodesFailClosed()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();

        using var unauthenticated = await client.GetAsync(Endpoint(Guid.NewGuid()));
        SetBearer(client, "not-a-valid-token");
        using var invalidBearer = await client.GetAsync(Endpoint(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidBearer.StatusCode);

        var authentication = await AuthenticateAsync(factory, client, "phase94-conceal");
        SetBearer(client, authentication.AccessToken);
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var incomplete = PreTriageSession.CreateForPatient(
            EntityId.From(authentication.Account.ProfileId),
            package.Questionnaire.Id,
            Now.AddDays(1),
            Now);
        var anonymous = PreTriageSession.CreateAnonymous(
            package.Questionnaire.Id,
            AnonymousCapabilityHash.FromHash(Guid.NewGuid().ToString("N")),
            Now.AddDays(1),
            Now);
        var unclaimed = PreTriageEpisode.CreateFrom(
            anonymous,
            package.RuleSet.Id,
            Now.AddMinutes(1),
            Now.AddHours(24));
        await using (var seed = CreateDbContext())
        {
            seed.AddRange(incomplete, anonymous, unclaimed);
            await seed.SaveChangesAsync();
        }

        using var unknown = await client.GetAsync(Endpoint(Guid.NewGuid()));
        using var incompleteResponse = await client.GetAsync(Endpoint(incomplete.Id.Value));
        using var unclaimedResponse = await client.GetAsync(Endpoint(unclaimed.Id.Value));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, incompleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unclaimedResponse.StatusCode);
        var unknownProblem = await ProblemAsync(unknown);
        Assert.Equal(unknownProblem, await ProblemAsync(incompleteResponse));
        Assert.Equal(unknownProblem, await ProblemAsync(unclaimedResponse));
    }

    [Theory]
    [InlineData("HEADACHE")]
    [InlineData("ABDOMINAL_PAIN")]
    [InlineData("FEVER")]
    [InlineData("CHEST_PAIN")]
    public async Task AuthorizedEpisodeReturnsExactApprovedAndreaPackage(string pathwayValue)
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(
            factory,
            client,
            $"phase94-{pathwayValue.ToLowerInvariant()}");
        SetBearer(client, authentication.AccessToken);
        var pathway = ClinicalPathwayCode.Create(pathwayValue);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            pathway);

        using var response = await client.GetAsync(Endpoint(episode.Id.Value));
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertExactContentAsync(document.RootElement, episode.Id, pathway);
        AssertNoInterpretedContractFields(document.RootElement);
    }

    [Fact]
    public async Task ClaimedEpisodeAndActiveManagerAreAuthorizedButRevocationIsImmediate()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        using var unrelatedClient = factory.CreateApiClient();
        var manager = await AuthenticateAsync(factory, client, "phase94-manager");
        var unrelated = await AuthenticateAsync(factory, unrelatedClient, "phase94-unrelated");
        SetBearer(client, manager.AccessToken);
        SetBearer(unrelatedClient, unrelated.AccessToken);

        var claimed = await SeedSupportedEpisodeAsync(
            EntityId.From(manager.Account.ProfileId),
            ClinicalPathways.Fever,
            claimed: true);
        using var claimedResponse = await client.GetAsync(Endpoint(claimed.Id.Value));
        Assert.Equal(HttpStatusCode.OK, claimedResponse.StatusCode);

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
            AuthorizationAttestation.Create("phase-9.4-content", Now),
            Now);
        await using (var seed = CreateDbContext())
        {
            seed.AddRange(managedPatient, relationship);
            await seed.SaveChangesAsync();
        }

        var managedEpisode = await SeedSupportedEpisodeAsync(
            managedPatient.Id,
            ClinicalPathways.ChestPain);
        using var managedResponse = await client.GetAsync(Endpoint(managedEpisode.Id.Value));
        using var unrelatedResponse = await unrelatedClient.GetAsync(
            Endpoint(managedEpisode.Id.Value));
        using var absentResponse = await unrelatedClient.GetAsync(Endpoint(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, managedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unrelatedResponse.StatusCode);
        Assert.Equal(await ProblemAsync(absentResponse), await ProblemAsync(unrelatedResponse));

        await using (var revoke = CreateDbContext())
        {
            var persisted = await revoke.CareRelationships.SingleAsync(
                value => value.Id == relationship.Id);
            persisted.Revoke(EntityId.From(manager.Account.AccountId), Now.AddMinutes(30));
            await revoke.SaveChangesAsync();
        }

        using var revoked = await client.GetAsync(Endpoint(managedEpisode.Id.Value));
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
    }

    [Fact]
    public async Task OtherSymptomsInactiveAndUnapprovedPackagesNeverFallback()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase94-unavailable");
        SetBearer(client, authentication.AccessToken);
        var patientId = EntityId.From(authentication.Account.ProfileId);
        var other = await SeedSupportedEpisodeAsync(patientId, ClinicalPathways.OtherSymptoms);

        var inactive = ReidentifiedPackage(
            ClinicalPathways.RespiratorySymptoms,
            "inactive",
            ClinicalContentStatus.MedicalTeamApproved,
            AndreaSymptomDiaryPackages.ApprovalEffectiveAt,
            activatedAt: null);
        var unapproved = ReidentifiedPackage(
            ClinicalPathways.BackPain,
            "unapproved",
            new ClinicalContentStatus(
                ClinicalContentSource.MedicalTeamProvided,
                ClinicalReviewStatus.Provisional,
                ClinicalApprovalStatus.PendingFormalReview),
            approvedAt: null,
            activatedAt: null);
        await ImportPackageAsync(inactive);
        await ImportPackageAsync(unapproved);
        var inactiveEpisode = await SeedCustomPathwayEpisodeAsync(
            patientId,
            ClinicalPathways.RespiratorySymptoms,
            "inactive");
        var unapprovedEpisode = await SeedCustomPathwayEpisodeAsync(
            patientId,
            ClinicalPathways.BackPain,
            "unapproved");

        foreach (var episode in new[] { other, inactiveEpisode, unapprovedEpisode })
        {
            using var response = await client.GetAsync(Endpoint(episode.Id.Value));
            var problem = await ProblemAsync(response);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("symptom_diary.content_unavailable", problem.ErrorCode);
        }
    }

    [Fact]
    public async Task IntegrityFailureReturnsSafeUnavailableProblemWithoutPackageDetails()
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
        var authentication = await AuthenticateAsync(factory, client, "phase94-integrity");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            ClinicalPathways.Headache);

        using var response = await client.GetAsync(Endpoint(episode.Id.Value));
        var raw = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemResponse>(
            raw,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("symptom_diary.content_unavailable", problem!.ErrorCode);
        Assert.DoesNotContain("internal-package-secret", raw, StringComparison.Ordinal);
        Assert.Equal(ClinicalPathways.Headache, throwingProvider.RequestedPathway);
    }

    [Fact]
    public async Task RepeatedGetIsReadOnlyAndLeavesEpisodeAndAllPatientRecordsUnchanged()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase94-readonly");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            ClinicalPathways.Fever);
        var before = await ReadOnlySnapshotAsync(episode.Id);

        using var first = await client.GetAsync(Endpoint(episode.Id.Value));
        using var second = await client.GetAsync(Endpoint(episode.Id.Value));
        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstBody, secondBody);
        Assert.Equal(before, await ReadOnlySnapshotAsync(episode.Id));
    }

    [Fact]
    public async Task QueryAndBodySelectorsAreRejectedWithoutContentLookup()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase94-selectors");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId),
            ClinicalPathways.Headache);

        using var query = await client.GetAsync(
            $"{Endpoint(episode.Id.Value)}?pathway=FEVER");
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(episode.Id.Value))
        {
            Content = JsonContent.Create(new { packageVersion = "forged" })
        };
        using var body = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, query.StatusCode);
        Assert.Equal("symptom_diary.unsupported_query", (await ProblemAsync(query)).ErrorCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, body.StatusCode);
        Assert.Equal("symptom_diary.unsupported_body", (await ProblemAsync(body)).ErrorCode);
    }

    [Fact]
    public async Task OpenApiAddsOnlyTheBearerSecuredContentRetrievalPath()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var operation = paths.GetProperty(
            "/api/v1/pre-triage/episodes/{episodeId}/symptom-diary-content")
            .GetProperty("get");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(56, paths.EnumerateObject().Count());
        var checkInPath = paths.GetProperty(
            "/api/v1/pre-triage/episodes/{episodeId}/check-ins");
        Assert.True(checkInPath.TryGetProperty("post", out _));
        Assert.True(checkInPath.TryGetProperty("get", out _));
        Assert.Contains(operation.GetProperty("security").EnumerateArray(), value =>
            value.TryGetProperty("Bearer", out _));
        var parameter = Assert.Single(operation.GetProperty("parameters").EnumerateArray());
        Assert.Equal("episodeId", parameter.GetProperty("name").GetString());
        Assert.Equal("path", parameter.GetProperty("in").GetString());
        Assert.True(parameter.GetProperty("required").GetBoolean());
        Assert.Equal(
            "uuid",
            parameter.GetProperty("schema").GetProperty("format").GetString());
        Assert.Equal(
            ["200", "401", "404", "422", "500"],
            operation.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name)
                .OrderBy(value => value));

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var responseProperties = schemas.GetProperty("SymptomDiaryContentResponse")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(value => value.Name)
            .OrderBy(value => value)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "contentHash", "episodeId", "information", "packageCode",
                "packageVersion", "packageVersionId", "pathway", "provenance",
                "questionSet"
            }.OrderBy(value => value),
            responseProperties);
        Assert.Equal(
            ["answerSchema", "code", "isRequired", "options", "prompt", "sourceOrder"],
            schemas.GetProperty("SymptomDiaryQuestionResponse")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(value => value.Name)
                .OrderBy(value => value));
    }

    private async Task AssertExactContentAsync(
        JsonElement root,
        EntityId episodeId,
        ClinicalPathwayCode pathway)
    {
        var expected = AndreaSymptomDiaryPackages.Create(pathway);
        await using var dbContext = CreateDbContext();
        var packageVersionId = await dbContext.SymptomDiaryPackageVersions
            .Where(value =>
                value.PackageCode == expected.PackageCode &&
                value.PackageVersion == expected.PackageVersion)
            .Select(value => value.Id.Value)
            .SingleAsync();

        Assert.Equal(episodeId.Value, root.GetProperty("episodeId").GetGuid());
        Assert.Equal(pathway.Value, root.GetProperty("pathway").GetString());
        Assert.Equal(packageVersionId, root.GetProperty("packageVersionId").GetGuid());
        Assert.Equal(expected.PackageCode.Value, root.GetProperty("packageCode").GetString());
        Assert.Equal(expected.PackageVersion.Value,
            root.GetProperty("packageVersion").GetString());
        Assert.Equal(expected.ExpectedContentHash!.Value,
            root.GetProperty("contentHash").GetString());

        var provenance = root.GetProperty("provenance");
        Assert.Equal("MEDICAL_TEAM_PROVIDED", provenance.GetProperty("source").GetString());
        Assert.Equal("REVIEWED", provenance.GetProperty("reviewStatus").GetString());
        Assert.Equal("APPROVED", provenance.GetProperty("approvalStatus").GetString());
        Assert.Equal(expected.ApprovedAt,
            provenance.GetProperty("approvedAt").GetDateTimeOffset());
        Assert.False(provenance.TryGetProperty("sourceReference", out _));
        Assert.False(provenance.TryGetProperty("importedAt", out _));

        var questionSet = root.GetProperty("questionSet");
        Assert.Equal(expected.QuestionSetCode.Value, questionSet.GetProperty("code").GetString());
        Assert.Equal(expected.QuestionSetVersion.Value,
            questionSet.GetProperty("version").GetString());
        var questions = questionSet.GetProperty("questions").EnumerateArray().ToArray();
        Assert.Equal(expected.Questions.Count, questions.Length);
        for (var questionIndex = 0; questionIndex < questions.Length; questionIndex++)
        {
            var actualQuestion = questions[questionIndex];
            var expectedQuestion = expected.Questions[questionIndex];
            Assert.Equal(expectedQuestion.Code.Value,
                actualQuestion.GetProperty("code").GetString());
            Assert.Equal(expectedQuestion.PromptText,
                actualQuestion.GetProperty("prompt").GetString());
            Assert.Equal(expectedQuestion.SourceOrder,
                actualQuestion.GetProperty("sourceOrder").GetInt32());
            Assert.Equal(expectedQuestion.IsRequired,
                actualQuestion.GetProperty("isRequired").GetBoolean());
            Assert.True(JsonNode.DeepEquals(
                JsonNode.Parse(expectedQuestion.AnswerSchemaJson),
                JsonNode.Parse(actualQuestion.GetProperty("answerSchema").GetRawText())));
            var options = actualQuestion.GetProperty("options").EnumerateArray().ToArray();
            Assert.Equal(expectedQuestion.Options.Count, options.Length);
            for (var optionIndex = 0; optionIndex < options.Length; optionIndex++)
            {
                var actualOption = options[optionIndex];
                var expectedOption = expectedQuestion.Options[optionIndex];
                Assert.Equal(expectedOption.Code.Value,
                    actualOption.GetProperty("code").GetString());
                Assert.Equal(expectedOption.Value,
                    actualOption.GetProperty("value").GetString());
                Assert.Equal(expectedOption.DisplayText,
                    actualOption.GetProperty("displayText").GetString());
                Assert.Equal(expectedOption.SourceOrder,
                    actualOption.GetProperty("sourceOrder").GetInt32());
            }
        }

        var information = root.GetProperty("information");
        Assert.Equal(expected.SymptomInformationCode.Value,
            information.GetProperty("code").GetString());
        Assert.Equal(expected.SymptomInformationVersion.Value,
            information.GetProperty("version").GetString());
        Assert.Equal(expected.InformationalHeading,
            information.GetProperty("heading").GetString());
        Assert.False(information.TryGetProperty("body", out _));
        var warnings = information.GetProperty("warningSigns").EnumerateArray().ToArray();
        Assert.Equal(expected.WarningSigns.Count, warnings.Length);
        for (var warningIndex = 0; warningIndex < warnings.Length; warningIndex++)
        {
            var actualWarning = warnings[warningIndex];
            var expectedWarning = expected.WarningSigns[warningIndex];
            Assert.Equal(expectedWarning.Code.Value,
                actualWarning.GetProperty("code").GetString());
            Assert.Equal(expectedWarning.DisplayText,
                actualWarning.GetProperty("displayText").GetString());
            Assert.Equal(expectedWarning.SourceOrder,
                actualWarning.GetProperty("sourceOrder").GetInt32());
        }

        if (pathway == ClinicalPathways.Fever)
        {
            var display = questions.SelectMany(question =>
                    question.GetProperty("options").EnumerateArray())
                .Select(option => option.GetProperty("displayText").GetString())
                .ToArray();
            Assert.Contains("≥40.0°C", display);
            Assert.Contains("38.0–38.9°C", display);
        }
    }

    private static void AssertNoInterpretedContractFields(JsonElement root)
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "assessment", "trend", "improving", "worsening", "riskScore", "urgency",
            "disposition", "redFlagDetected", "matchedWarningSigns", "recommendation",
            "action", "escalation", "nextCheckInAt", "reminder", "patientAnswer"
        };
        Assert.DoesNotContain(PropertyNames(root), forbidden.Contains);
    }

    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var nested in PropertyNames(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in PropertyNames(item))
                {
                    yield return nested;
                }
            }
        }
    }

    private async Task<PreTriageEpisode> SeedSupportedEpisodeAsync(
        EntityId patientProfileId,
        ClinicalPathwayCode pathway,
        bool claimed = false)
    {
        var package = SimplifiedDemoDefinitionPackages.Create(pathway);
        PreTriageSession session;
        PreTriageEpisode episode;
        if (claimed)
        {
            session = PreTriageSession.CreateAnonymous(
                package.Questionnaire.Id,
                AnonymousCapabilityHash.FromHash(Guid.NewGuid().ToString("N")),
                Now.AddDays(1),
                Now);
            episode = PreTriageEpisode.CreateFrom(
                session,
                package.RuleSet.Id,
                Now.AddMinutes(1),
                Now.AddHours(24));
            Assert.True(episode.Claim(patientProfileId, Now.AddMinutes(2)));
        }
        else
        {
            session = PreTriageSession.CreateForPatient(
                patientProfileId,
                package.Questionnaire.Id,
                Now.AddDays(1),
                Now);
            episode = PreTriageEpisode.CreateFrom(
                session,
                package.RuleSet.Id,
                Now.AddMinutes(1));
        }

        await using var dbContext = CreateDbContext();
        dbContext.AddRange(session, episode);
        await dbContext.SaveChangesAsync();
        return episode;
    }

    private async Task<PreTriageEpisode> SeedCustomPathwayEpisodeAsync(
        EntityId patientProfileId,
        ClinicalPathwayCode pathway,
        string suffix)
    {
        var unique = Guid.NewGuid().ToString("N");
        var version = DefinitionVersion.Create($"phase94-{suffix}-{unique[..8]}");
        var questionnaire = QuestionnaireDefinitionVersion.Import(
            pathway,
            QuestionnaireCode.Create($"phase94-{suffix}-q-{unique}"),
            version,
            DefinitionHash.FromHash(new string('a', 64)),
            ClinicalContentStatus.NonClinicalDemo,
            Now,
            activatedAt: Now);
        var ruleSet = ClinicalRuleSetVersion.Import(
            pathway,
            RuleSetCode.Create($"phase94-{suffix}-r-{unique}"),
            version,
            DefinitionHash.FromHash(new string('b', 64)),
            ClinicalContentStatus.NonClinicalDemo,
            "{}",
            Now,
            activatedAt: Now);
        var session = PreTriageSession.CreateForPatient(
            patientProfileId,
            questionnaire.Id,
            Now.AddDays(1),
            Now);
        var episode = PreTriageEpisode.CreateFrom(
            session,
            ruleSet.Id,
            Now.AddMinutes(1));
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(questionnaire, ruleSet, session, episode);
        await dbContext.SaveChangesAsync();
        return episode;
    }

    private static SymptomDiaryPackageDefinition ReidentifiedPackage(
        ClinicalPathwayCode pathway,
        string suffix,
        ClinicalContentStatus status,
        DateTimeOffset? approvedAt,
        DateTimeOffset? activatedAt)
    {
        var source = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        var unique = Guid.NewGuid().ToString("N");
        return source with
        {
            PackageCode = Beeexy.Domain.Care.SymptomDiaryCode.Create(
                $"phase94-{suffix}-package-{unique}"),
            QuestionSetCode = Beeexy.Domain.Care.SymptomDiaryCode.Create(
                $"phase94-{suffix}-questions-{unique}"),
            SymptomInformationCode = Beeexy.Domain.Care.SymptomDiaryCode.Create(
                $"phase94-{suffix}-information-{unique}"),
            PackageVersion = DefinitionVersion.Create($"phase94-{suffix}"),
            QuestionSetVersion = DefinitionVersion.Create($"phase94-{suffix}"),
            SymptomInformationVersion = DefinitionVersion.Create($"phase94-{suffix}"),
            Pathway = pathway,
            ContentStatus = status,
            ApprovedAt = approvedAt,
            ActivatedAt = activatedAt,
            ExpectedContentHash = null
        };
    }

    private async Task ImportApprovedContentAsync()
    {
        await using var dbContext = CreateDbContext();
        var definitionImporter = new ClinicalDefinitionImporter(
            dbContext,
            new ClinicalDefinitionPackageValidator(),
            NullLogger<ClinicalDefinitionImporter>.Instance);
        foreach (var package in SimplifiedDemoDefinitionPackages.CreateAll())
        {
            await definitionImporter.ImportAsync(package);
        }

        var diaryImporter = CreateDiaryImporter(dbContext);
        foreach (var package in AndreaSymptomDiaryPackages.CreateAll())
        {
            await diaryImporter.ImportAsync(package);
        }
    }

    private async Task ImportPackageAsync(SymptomDiaryPackageDefinition package)
    {
        await using var dbContext = CreateDbContext();
        await CreateDiaryImporter(dbContext).ImportAsync(package);
    }

    private static SymptomDiaryContentImporter CreateDiaryImporter(
        BeeexyDbContext dbContext)
    {
        var serializer = new SymptomDiaryPackageCanonicalSerializer();
        return new SymptomDiaryContentImporter(
            dbContext,
            new SymptomDiaryPackageValidator(),
            serializer,
            new SymptomDiaryPackageHashCalculator(serializer),
            NullLogger<SymptomDiaryContentImporter>.Instance);
    }

    private async Task<ReadOnlySnapshot> ReadOnlySnapshotAsync(EntityId episodeId)
    {
        await using var dbContext = CreateDbContext();
        var episode = await dbContext.PreTriageEpisodes.AsNoTracking()
            .SingleAsync(value => value.Id == episodeId);
        return new ReadOnlySnapshot(
            $"{episode.Id}|{episode.SourceSessionId}|{episode.PatientProfileId}|" +
            $"{episode.QuestionnaireVersionId}|{episode.ClinicalRuleSetVersionId}|" +
            $"{episode.CompletedAt:O}|{episode.ClaimedAt:O}",
            await dbContext.SymptomCheckIns.CountAsync(),
            await dbContext.SymptomCheckInAnswers.CountAsync(),
            await dbContext.ClinicalHistoryEvents.CountAsync(),
            await dbContext.ClinicalAssessments.CountAsync(),
            await dbContext.FhirExports.CountAsync(),
            await dbContext.AiAnalysisRequests.CountAsync());
    }

    private async Task<AuthenticationResult> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client,
        string prefix)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        using var challenge = await client.PostAsJsonAsync(
            "/api/v1/auth/email/challenges",
            new { email });
        Assert.Equal(HttpStatusCode.Accepted, challenge.StatusCode);
        var message = Assert.Single(
            factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>().Messages,
            value => value.Recipient.Value == email);
        using var verification = await client.PostAsJsonAsync(
            "/api/v1/auth/email/verify",
            new { email, code = message.OneTimeCode });
        verification.EnsureSuccessStatusCode();
        return Assert.IsType<AuthenticationResult>(
            await verification.Content.ReadFromJsonAsync<AuthenticationResult>());
    }

    private static async Task<ProblemResponse> ProblemAsync(HttpResponseMessage response) =>
        Assert.IsType<ProblemResponse>(
            await response.Content.ReadFromJsonAsync<ProblemResponse>());

    private static string Endpoint(Guid episodeId) =>
        $"/api/v1/pre-triage/episodes/{episodeId:D}/symptom-diary-content";

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\";";
        await command.ExecuteNonQueryAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class ThrowingContentProvider : ISymptomDiaryContentProvider
    {
        public ClinicalPathwayCode? RequestedPathway { get; private set; }

        public Task<SymptomDiaryPackageContent?> GetActivePackageAsync(
            ClinicalPathwayCode pathway,
            CancellationToken cancellationToken = default)
        {
            RequestedPathway = pathway;
            throw new SymptomDiaryPackageIntegrityException("internal-package-secret");
        }

        public Task<SymptomDiaryPackageContent?> GetExactPackageAsync(
            EntityId packageVersionId,
            CancellationToken cancellationToken = default) =>
            throw new SymptomDiaryPackageIntegrityException("internal-package-secret");
    }

    private sealed record AuthenticationResult(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);

    private sealed record ProblemResponse(
        int Status,
        string Title,
        string? Type,
        string? Detail,
        string? ErrorCode);

    private sealed record ReadOnlySnapshot(
        string Episode,
        int CheckIns,
        int CheckInAnswers,
        int HistoryEvents,
        int Assessments,
        int FhirExports,
        int AiAnalysisRequests);
}
