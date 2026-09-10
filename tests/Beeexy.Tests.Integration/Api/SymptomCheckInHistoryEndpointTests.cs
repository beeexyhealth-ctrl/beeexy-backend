using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Beeexy.Application.Care;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beeexy.Tests.Integration.Api;

public sealed partial class SymptomDiaryContentEndpointTests
{
    [Fact]
    [Trait("Category", "Phase96")]
    public async Task HistoryRequiresAuthenticationAndConcealsUnknownAndCrossAccountEpisodes()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var ownerClient = factory.CreateApiClient();
        using var otherClient = factory.CreateApiClient();
        using var anonymous = await ownerClient.GetAsync(CheckInEndpoint(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var owner = await AuthenticateAsync(factory, ownerClient, "phase96-owner");
        var other = await AuthenticateAsync(factory, otherClient, "phase96-other");
        SetBearer(ownerClient, owner.AccessToken);
        SetBearer(otherClient, other.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(owner.Account.ProfileId), ClinicalPathways.Headache);

        using var empty = await ownerClient.GetAsync(CheckInEndpoint(episode.Id.Value));
        using var unknown = await ownerClient.GetAsync(CheckInEndpoint(Guid.NewGuid()));
        using var unknownWithInvalidPaging = await ownerClient.GetAsync(
            CheckInEndpoint(Guid.NewGuid()) + "?pageSize=nope&cursor=invalid");
        using var crossAccount = await otherClient.GetAsync(CheckInEndpoint(episode.Id.Value));
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        var page = await empty.Content.ReadFromJsonAsync<HistoryPageResponse>();
        Assert.Empty(page!.Items);
        Assert.Null(page.NextCursor);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownWithInvalidPaging.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossAccount.StatusCode);
        var unknownProblem = await ProblemAsync(unknown);
        var crossAccountProblem = await ProblemAsync(crossAccount);
        var invalidPagingProblem = await ProblemAsync(unknownWithInvalidPaging);
        Assert.Equal(unknownProblem, crossAccountProblem);
        Assert.Equal(crossAccountProblem, invalidPagingProblem);
    }

    [Fact]
    [Trait("Category", "Phase96")]
    public async Task OneEntryRoundTripsExactFrozenContentValuesAndRepeatedGetIsReadOnly()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase96-roundtrip");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId), ClinicalPathways.Headache);
        var definition = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        var answers = AllAnswers(definition);
        using var create = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(await PackageIdAsync(ClinicalPathways.Headache), Guid.NewGuid(), answers));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var before = await SideEffectSnapshotAsync(episode.Id);

        using var first = await client.GetAsync(CheckInEndpoint(episode.Id.Value));
        using var second = await client.GetAsync(CheckInEndpoint(episode.Id.Value));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var document = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        AssertNoInterpretedContractFields(item);
        Assert.Equal(episode.Id.Value, item.GetProperty("episodeId").GetGuid());
        Assert.Equal(definition.PackageCode.Value, item.GetProperty("packageCode").GetString());
        Assert.Equal(definition.ExpectedContentHash!.Value,
            item.GetProperty("contentHash").GetString());
        Assert.Equal(definition.QuestionSetCode.Value,
            item.GetProperty("questionSet").GetProperty("code").GetString());
        Assert.Equal(definition.SymptomInformationCode.Value,
            item.GetProperty("information").GetProperty("code").GetString());
        Assert.Equal(definition.WarningSigns.Select(warning => warning.DisplayText),
            item.GetProperty("information").GetProperty("warningSigns")
                .EnumerateArray().Select(warning => warning.GetProperty("displayText").GetString()));
        var returnedAnswers = item.GetProperty("answers").EnumerateArray().ToArray();
        Assert.Equal(definition.Questions.Select(question => question.PromptText),
            returnedAnswers.Select(answer => answer.GetProperty("prompt").GetString()));
        for (var index = 0; index < returnedAnswers.Length; index++)
        {
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                answers[index]["value"],
                System.Text.Json.Nodes.JsonNode.Parse(
                    returnedAnswers[index].GetProperty("value").GetRawText())));
        }

        Assert.Equal(before, await SideEffectSnapshotAsync(episode.Id));
    }

    [Fact]
    [Trait("Category", "Phase96")]
    public async Task DefaultAndMaximumPagesTraverseTiedEntriesOldestFirstExactlyOnce()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase96-pages");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId), ClinicalPathways.Headache);
        var expected = await SeedHistoryAsync(
            episode.Id,
            EntityId.From(authentication.Account.AccountId),
            23,
            tieFirstTwo: true);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var uri = CheckInEndpoint(episode.Id.Value) +
                (cursor is null ? string.Empty : $"?cursor={Uri.EscapeDataString(cursor)}");
            using var response = await client.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<HistoryPageResponse>();
            seen.AddRange(page!.Items.Select(item => item.CheckInId));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(23, seen.Count);
        Assert.Equal(expected, seen);
        Assert.Equal(seen.Count, seen.Distinct().Count());

        using var maximum = await client.GetAsync(
            CheckInEndpoint(episode.Id.Value) + "?pageSize=100");
        var maximumPage = await maximum.Content.ReadFromJsonAsync<HistoryPageResponse>();
        Assert.Equal(23, maximumPage!.Items.Count);
        Assert.Null(maximumPage.NextCursor);
    }

    [Theory]
    [InlineData("?pageSize=0", "symptom_diary.page_size_invalid")]
    [InlineData("?pageSize=101", "symptom_diary.page_size_invalid")]
    [InlineData("?pageSize=nope", "symptom_diary.page_size_invalid")]
    [InlineData("?cursor=not+a+cursor", "symptom_diary.cursor_invalid")]
    [InlineData("?cursor=a&cursor=b", "symptom_diary.unsupported_query")]
    [InlineData("?trend=true", "symptom_diary.unsupported_query")]
    [Trait("Category", "Phase96")]
    public async Task InvalidHistorySelectorsReturnSafeValidation(
        string query,
        string errorCode)
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase96-invalid");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId), ClinicalPathways.Headache);

        using var response = await client.GetAsync(CheckInEndpoint(episode.Id.Value) + query);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(errorCode, (await ProblemAsync(response)).ErrorCode);
    }

    [Fact]
    [Trait("Category", "Phase96")]
    public async Task CursorRejectsTamperingPageSizeMismatchAndCrossEpisodeReplay()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase96-cursors");
        SetBearer(client, authentication.AccessToken);
        var owner = EntityId.From(authentication.Account.ProfileId);
        var firstEpisode = await SeedSupportedEpisodeAsync(owner, ClinicalPathways.Headache);
        var secondEpisode = await SeedSupportedEpisodeAsync(owner, ClinicalPathways.Headache);
        var accountId = EntityId.From(authentication.Account.AccountId);
        await SeedHistoryAsync(firstEpisode.Id, accountId, 3);

        using var firstResponse = await client.GetAsync(
            CheckInEndpoint(firstEpisode.Id.Value) + "?pageSize=1");
        var first = await firstResponse.Content.ReadFromJsonAsync<HistoryPageResponse>();
        var cursor = first!.NextCursor!;
        var tampered = cursor[..^1] + (cursor[^1] == 'A' ? 'B' : 'A');
        var invalidUris = new[]
        {
            CheckInEndpoint(firstEpisode.Id.Value) +
                $"?pageSize=1&cursor={Uri.EscapeDataString(tampered)}",
            CheckInEndpoint(firstEpisode.Id.Value) +
                $"?pageSize=2&cursor={Uri.EscapeDataString(cursor)}",
            CheckInEndpoint(secondEpisode.Id.Value) +
                $"?pageSize=1&cursor={Uri.EscapeDataString(cursor)}"
        };

        foreach (var uri in invalidUris)
        {
            using var response = await client.GetAsync(uri);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("symptom_diary.cursor_invalid", (await ProblemAsync(response)).ErrorCode);
        }
    }

    [Fact]
    [Trait("Category", "Phase96")]
    public async Task HistoricalPackageAStillRendersAfterDifferentPackageBActivates()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase96-frozen");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId), ClinicalPathways.Headache);
        var packageA = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        var packageAId = await PackageIdAsync(ClinicalPathways.Headache);
        using var create = await client.PostAsJsonAsync(
            CheckInEndpoint(episode.Id.Value),
            Request(packageAId, Guid.NewGuid(), AllAnswers(packageA)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var packageB = ReidentifiedPackage(
            ClinicalPathways.Headache,
            "history-b",
            ClinicalContentStatus.MedicalTeamApproved,
            Now.AddMinutes(5),
            Now.AddMinutes(6));
        packageB = packageB with
        {
            Questions = packageB.Questions.Select((question, index) => index == 0
                ? question with { PromptText = "Texto posterior que no debe reinterpretar A" }
                : question).ToArray()
        };
        await ImportPackageAsync(packageB);

        using var active = await client.GetAsync(Endpoint(episode.Id.Value));
        using var history = await client.GetAsync(CheckInEndpoint(episode.Id.Value));
        using var activeJson = JsonDocument.Parse(await active.Content.ReadAsStringAsync());
        using var historyJson = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        var item = Assert.Single(historyJson.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(packageB.PackageCode.Value,
            activeJson.RootElement.GetProperty("packageCode").GetString());
        Assert.Equal(packageAId, item.GetProperty("packageVersionId").GetGuid());
        Assert.Equal(packageA.PackageCode.Value, item.GetProperty("packageCode").GetString());
        Assert.Equal(packageA.Questions[0].PromptText,
            item.GetProperty("answers")[0].GetProperty("prompt").GetString());
        Assert.DoesNotContain("Texto posterior", await history.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Phase96")]
    public async Task RevokedManagerIsDeniedAndCrossPathwayFrozenPackageFailsClosedWithoutFallback()
    {
        await ImportApprovedContentAsync();
        using var factory = new BeeexyApiFactory(ConnectionString);
        using var client = factory.CreateApiClient();
        var manager = await AuthenticateAsync(factory, client, "phase96-manager");
        SetBearer(client, manager.AccessToken);
        var patient = PatientProfile.CreateManaged(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            PatientName.Create("Ana"), PatientName.Create("Rios"),
            new DateOnly(2010, 2, 3), SexAssignedAtBirth.Female,
            UsState.Create("NY"), Now);
        var relationship = CareRelationship.Create(
            EntityId.From(manager.Account.ProfileId), patient.Id,
            CareRelationshipType.Caregiver,
            EntityId.From(manager.Account.AccountId),
            AuthorizationAttestation.Create("phase-9.6-history", Now), Now);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(patient, relationship);
            await dbContext.SaveChangesAsync();
        }

        var managedEpisode = await SeedSupportedEpisodeAsync(patient.Id, ClinicalPathways.Headache);
        using var allowed = await client.GetAsync(CheckInEndpoint(managedEpisode.Id.Value));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        await using (var dbContext = CreateDbContext())
        {
            var persisted = await dbContext.CareRelationships.SingleAsync(
                value => value.Id == relationship.Id);
            persisted.Revoke(EntityId.From(manager.Account.AccountId), Now.AddMinutes(1));
            await dbContext.SaveChangesAsync();
        }

        using var revoked = await client.GetAsync(CheckInEndpoint(managedEpisode.Id.Value));
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);

        using var ownerClient = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, ownerClient, "phase96-other-symptoms");
        SetBearer(ownerClient, owner.AccessToken);
        var otherEpisode = await SeedCustomPathwayEpisodeAsync(
            EntityId.From(owner.Account.ProfileId), ClinicalPathways.OtherSymptoms, "history");
        var headachePackage = await PackageIdAsync(ClinicalPathways.Headache);
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO care.symptom_check_ins
                    (id, episode_id, package_version_id, submitting_account_id,
                     created_at, idempotency_key, canonical_request_hash)
                VALUES
                    ({Guid.NewGuid()}, {otherEpisode.Id.Value}, {headachePackage},
                     {owner.Account.AccountId}, {Now}, {Guid.NewGuid()}, {new string('a', 64)})
                """);
        }

        using var wrongPath = await ownerClient.GetAsync(CheckInEndpoint(otherEpisode.Id.Value));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongPath.StatusCode);
        Assert.Equal("symptom_diary.content_unavailable",
            (await ProblemAsync(wrongPath)).ErrorCode);
    }

    [Fact]
    [Trait("Category", "Phase96")]
    public async Task CorruptExactPackageFailureIsSanitizedAndOpenApiDocumentsOnlyHistoryGet()
    {
        await ImportApprovedContentAsync();
        var throwing = new ThrowingBatchProvider();
        using var factory = new BeeexyApiFactory(
            ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<ISymptomDiaryExactContentBatchProvider>();
                services.AddSingleton<ISymptomDiaryExactContentBatchProvider>(throwing);
            });
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "phase96-corrupt");
        SetBearer(client, authentication.AccessToken);
        var episode = await SeedSupportedEpisodeAsync(
            EntityId.From(authentication.Account.ProfileId), ClinicalPathways.Headache);
        await SeedHistoryAsync(
            episode.Id, EntityId.From(authentication.Account.AccountId), 1);

        using var corrupt = await client.GetAsync(CheckInEndpoint(episode.Id.Value));
        var raw = await corrupt.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, corrupt.StatusCode);
        Assert.DoesNotContain("internal-package-secret", raw, StringComparison.Ordinal);
        Assert.Equal("symptom_diary.content_unavailable",
            JsonSerializer.Deserialize<ProblemResponse>(raw,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!.ErrorCode);

        using var swagger = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await swagger.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var checkIns = paths.GetProperty(
            "/api/v1/pre-triage/episodes/{episodeId}/check-ins");
        var get = checkIns.GetProperty("get");
        Assert.Equal(60, paths.EnumerateObject().Count());
        Assert.True(checkIns.TryGetProperty("post", out _));
        Assert.Contains(get.GetProperty("security").EnumerateArray(), value =>
            value.TryGetProperty("Bearer", out _));
        Assert.Equal(["cursor", "episodeId", "pageSize"],
            get.GetProperty("parameters").EnumerateArray()
                .Select(parameter => parameter.GetProperty("name").GetString())
                .OrderBy(name => name));
        var pageSize = get.GetProperty("parameters").EnumerateArray().Single(parameter =>
            parameter.GetProperty("name").GetString() == "pageSize");
        Assert.Equal("integer", pageSize.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal(["200", "401", "404", "422", "500"],
            get.GetProperty("responses").EnumerateObject()
                .Select(response => response.Name).OrderBy(name => name));
        Assert.False(paths.TryGetProperty(
            "/api/v1/pre-triage/episodes/{episodeId}/check-ins/{checkInId}", out _));
    }

    private async Task<IReadOnlyList<Guid>> SeedHistoryAsync(
        EntityId episodeId,
        EntityId submittingAccountId,
        int count,
        bool tieFirstTwo = false)
    {
        await using var dbContext = CreateDbContext();
        var episode = await dbContext.PreTriageEpisodes.SingleAsync(value => value.Id == episodeId);
        var questionnaire = await dbContext.QuestionnaireVersions.AsNoTracking().SingleAsync(
            value => value.Id == episode.QuestionnaireVersionId);
        var package = await dbContext.SymptomDiaryPackageVersions
            .AsSplitQuery()
            .Include(value => value.Questions)
            .ThenInclude(value => value.Options)
            .SingleAsync(value => value.Pathway == ClinicalPathways.Headache);
        var ids = Enumerable.Range(1, count)
            .Select(index => EntityId.From(Guid.Parse(
                $"10000000-0000-0000-0000-{index:000000000000}")))
            .ToArray();
        var values = ids.Select((id, index) => SymptomCheckIn.Create(
            episode,
            questionnaire,
            package,
            submittingAccountId,
            EntityId.New(),
            SymptomDiarySha256.FromHash(new string((char)('a' + index % 6), 64)),
            Now.AddMinutes(tieFirstTwo && index < 2 ? 0 : index),
            id: id)).ToArray();
        dbContext.SymptomCheckIns.AddRange(values);
        await dbContext.SaveChangesAsync();
        return values.OrderBy(value => value.CreatedAt)
            .ThenBy(value => value.Id.Value.ToString("D"), StringComparer.Ordinal)
            .Select(value => value.Id.Value)
            .ToArray();
    }

    private sealed class ThrowingBatchProvider : ISymptomDiaryExactContentBatchProvider
    {
        public Task<IReadOnlyDictionary<EntityId, SymptomDiaryPackageContent>>
            GetExactPackagesAsync(IReadOnlyCollection<EntityId> packageVersionIds,
                CancellationToken cancellationToken = default) =>
            throw new SymptomDiaryPackageIntegrityException("internal-package-secret");
    }

    private sealed record HistoryPageResponse(
        IReadOnlyList<HistoryItemResponse> Items,
        string? NextCursor);

    private sealed record HistoryItemResponse(Guid CheckInId, DateTimeOffset CreatedAt);
}
