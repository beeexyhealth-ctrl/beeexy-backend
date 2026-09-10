using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Identity;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Sharing;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class ShareEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string Endpoint = "/api/v1/shares";
    private const string PublicBaseUrl = "https://frontend.share.test/open";
    private static readonly DateTimeOffset Now = Normalize(DateTimeOffset.UtcNow);

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task PrimaryCreation_PersistsOnlyHashAndListsSecretFreeMetadata()
    {
        await EnsureMigratedAsync();
        var logs = new InMemoryLoggerProvider();
        using var factory = CreateFactory(logs);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "primary-create");
        SetBearer(client, authentication.AccessToken);
        var key = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            ShareRequest("FullProfile", key));
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("FullProfile", root.GetProperty("scope").GetString());
        Assert.Equal(Now, root.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(Now.AddHours(24), root.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.False(root.GetProperty("capabilityPreviouslyIssued").GetBoolean());
        var capability = Assert.IsType<string>(root.GetProperty("capability").GetString());
        var shareUrl = Assert.IsType<string>(root.GetProperty("shareUrl").GetString());
        Assert.StartsWith("shc1.", capability);
        Assert.Equal(48, capability.Length);
        Assert.Equal($"{PublicBaseUrl}#{capability}", shareUrl);
        Assert.DoesNotContain('?', shareUrl);
        Assert.False(root.TryGetProperty("capabilityHash", out _));
        Assert.False(root.TryGetProperty("creatorAccountId", out _));

        await using (var dbContext = CreateDbContext())
        {
            var grant = await dbContext.ShareGrants.AsNoTracking().SingleAsync(
                value => value.IdempotencyKey == EntityId.From(key));
            Assert.Equal(EntityId.From(authentication.Account.ProfileId), grant.PatientProfileId);
            Assert.Equal(EntityId.From(authentication.Account.AccountId), grant.CreatorAccountId);
            Assert.Equal(Now.AddHours(24), grant.ExpiresAt);
            Assert.NotEqual(capability, grant.CapabilityHash.Value);
            Assert.DoesNotContain(capability, grant.CapabilityHash.Value, StringComparison.Ordinal);
            var createdEvent = await dbContext.ShareAccessEvents.AsNoTracking().SingleAsync(
                value => value.ShareGrantId == grant.Id);
            Assert.Equal(ShareAccessEventType.ShareCreated, createdEvent.EventType);
            Assert.Equal(ShareAccessOutcome.Succeeded, createdEvent.Outcome);
            Assert.Null(createdEvent.ResourceType);
        }

        using var listResponse = await client.GetAsync(Endpoint);
        var listBody = await listResponse.Content.ReadAsStringAsync();
        using var listDocument = JsonDocument.Parse(listBody);
        var listed = Assert.Single(listDocument.RootElement
            .GetProperty("shares")
            .EnumerateArray()
            .Where(item => item.GetProperty("shareGrantId").GetGuid() ==
                root.GetProperty("shareGrantId").GetGuid()));
        Assert.Equal("Active", listed.GetProperty("status").GetString());
        Assert.Equal(0, listed.GetProperty("itemCount").GetInt32());
        Assert.DoesNotContain(capability, listBody, StringComparison.Ordinal);
        Assert.DoesNotContain("capability", listBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", listBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shareUrl", listBody, StringComparison.OrdinalIgnoreCase);

        var logText = string.Join('\n', logs.Messages);
        Assert.DoesNotContain(capability, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(shareUrl, logText, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256:", logText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task LifetimeScopeShapeAndMalformedJsonValidation_FailBeforePersistence()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "validation");
        SetBearer(client, authentication.AccessToken);
        var initialCount = await CountPatientGrantsAsync(authentication.Account.ProfileId);
        var invalidRequests = new object[]
        {
            ShareRequest("Case", Guid.NewGuid()),
            ShareRequest("Visit", Guid.NewGuid()),
            ShareRequest("Unknown", Guid.NewGuid()),
            ShareRequest("FullProfile", Guid.NewGuid(), 0),
            ShareRequest("FullProfile", Guid.NewGuid(), -1),
            ShareRequest("FullProfile", Guid.NewGuid(), 10081),
            new { scope = "FullProfile", idempotencyKey = Guid.NewGuid(), permanent = true },
            new
            {
                scope = "FullProfile",
                idempotencyKey = Guid.NewGuid(),
                patientId = authentication.Account.ProfileId
            },
            new
            {
                scope = "FullProfile",
                idempotencyKey = Guid.NewGuid(),
                beeexyId = authentication.Account.BeeexyId
            }
        };

        foreach (var request in invalidRequests)
        {
            using var response = await client.PostAsJsonAsync(Endpoint, request);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }

        using var malformed = await client.PostAsync(
            Endpoint,
            new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(initialCount, await CountPatientGrantsAsync(authentication.Account.ProfileId));

        foreach (var minutes in new[] { 60, 10080 })
        {
            var key = Guid.NewGuid();
            using var accepted = await client.PostAsJsonAsync(
                Endpoint,
                ShareRequest("PreTriage", key, minutes));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
            using var acceptedDocument = JsonDocument.Parse(
                await accepted.Content.ReadAsStringAsync());
            Assert.Equal(
                Now.AddMinutes(minutes),
                acceptedDocument.RootElement.GetProperty("expiresAt").GetDateTimeOffset());
        }
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task Idempotency_SequentialConcurrentConflictAndDistinctKeysAreDatabaseBacked()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var firstClient = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, firstClient, "idempotency");
        SetBearer(firstClient, authentication.AccessToken);
        using var secondClient = factory.CreateApiClient();
        SetBearer(secondClient, authentication.AccessToken);
        var sequentialKey = Guid.NewGuid();

        using var first = await firstClient.PostAsJsonAsync(
            Endpoint,
            ShareRequest("FullProfile", sequentialKey));
        using var replay = await firstClient.PostAsJsonAsync(
            Endpoint,
            ShareRequest("FullProfile", sequentialKey));
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var replayDocument = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(
            firstDocument.RootElement.GetProperty("shareGrantId").GetGuid(),
            replayDocument.RootElement.GetProperty("shareGrantId").GetGuid());
        Assert.True(replayDocument.RootElement
            .GetProperty("capabilityPreviouslyIssued")
            .GetBoolean());
        Assert.False(replayDocument.RootElement.TryGetProperty("capability", out _));
        Assert.False(replayDocument.RootElement.TryGetProperty("shareUrl", out _));

        using var conflict = await firstClient.PostAsJsonAsync(
            Endpoint,
            ShareRequest("PreTriage", sequentialKey));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var concurrentKey = Guid.NewGuid();
        var sameKeyResponses = await Task.WhenAll(
            firstClient.PostAsJsonAsync(Endpoint, ShareRequest("FullProfile", concurrentKey)),
            secondClient.PostAsJsonAsync(Endpoint, ShareRequest("FullProfile", concurrentKey)));
        using (sameKeyResponses[0])
        using (sameKeyResponses[1])
        {
            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.Created],
                sameKeyResponses.Select(value => value.StatusCode).Order().ToArray());
            var bodies = await Task.WhenAll(
                sameKeyResponses[0].Content.ReadAsStringAsync(),
                sameKeyResponses[1].Content.ReadAsStringAsync());
            Assert.Single(bodies, value => value.Contains("\"capability\"", StringComparison.Ordinal));
        }

        var distinctKeys = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var distinctResponses = await Task.WhenAll(
            firstClient.PostAsJsonAsync(Endpoint, ShareRequest("FullProfile", distinctKeys[0])),
            secondClient.PostAsJsonAsync(Endpoint, ShareRequest("FullProfile", distinctKeys[1])));
        using (distinctResponses[0])
        using (distinctResponses[1])
        {
            Assert.All(distinctResponses, value => Assert.Equal(HttpStatusCode.Created, value.StatusCode));
        }

        await using var dbContext = CreateDbContext();
        var patientId = EntityId.From(authentication.Account.ProfileId);
        Assert.Equal(1, await dbContext.ShareGrants.CountAsync(value =>
            value.PatientProfileId == patientId && value.IdempotencyKey == EntityId.From(sequentialKey)));
        Assert.Equal(1, await dbContext.ShareGrants.CountAsync(value =>
            value.PatientProfileId == patientId && value.IdempotencyKey == EntityId.From(concurrentKey)));
        Assert.Equal(2, await dbContext.ShareGrants.CountAsync(value =>
            value.PatientProfileId == patientId && distinctKeys
                .Select(EntityId.From)
                .Contains(value.IdempotencyKey)));
        var relevantGrantIds = await dbContext.ShareGrants
            .Where(value => value.PatientProfileId == patientId &&
                (value.IdempotencyKey == EntityId.From(sequentialKey) ||
                 value.IdempotencyKey == EntityId.From(concurrentKey)))
            .Select(value => value.Id)
            .ToArrayAsync();
        Assert.Equal(2, await dbContext.ShareAccessEvents.CountAsync(value =>
            relevantGrantIds.Contains(value.ShareGrantId) &&
            value.EventType == ShareAccessEventType.ShareCreated));
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task SpecificRecords_ValidateSupportedTypeExactOwnershipAndAtomicItems()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var ownerClient = factory.CreateApiClient();
        using var foreignClient = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, ownerClient, "specific-owner");
        var foreign = await AuthenticateAsync(factory, foreignClient, "specific-foreign");
        SetBearer(ownerClient, owner.AccessToken);
        SetBearer(foreignClient, foreign.AccessToken);
        var ownerEpisode = await SeedEpisodeAsync(owner.Account.ProfileId, "owner");
        var foreignEpisode = await SeedEpisodeAsync(foreign.Account.ProfileId, "foreign");

        var key = Guid.NewGuid();
        using var success = await ownerClient.PostAsJsonAsync(
            Endpoint,
            SpecificRecordsRequest(key, ownerEpisode));
        Assert.Equal(HttpStatusCode.Created, success.StatusCode);
        using var successDocument = JsonDocument.Parse(await success.Content.ReadAsStringAsync());
        Assert.Equal(1, successDocument.RootElement.GetProperty("itemCount").GetInt32());

        await using (var dbContext = CreateDbContext())
        {
            var grant = await dbContext.ShareGrants.SingleAsync(
                value => value.IdempotencyKey == EntityId.From(key));
            var item = await dbContext.ShareGrantItems.SingleAsync(
                value => value.ShareGrantId == grant.Id);
            Assert.Equal(SupportedShareResourceTypes.PreTriageEpisode, item.ResourceType.Value);
            Assert.Equal(EntityId.From(ownerEpisode), item.ResourceId);
            Assert.Single(await dbContext.ShareAccessEvents.Where(
                value => value.ShareGrantId == grant.Id).ToArrayAsync());
        }

        using var foreignItem = await ownerClient.PostAsJsonAsync(
            Endpoint,
            SpecificRecordsRequest(Guid.NewGuid(), foreignEpisode));
        Assert.Equal(HttpStatusCode.NotFound, foreignItem.StatusCode);

        using var missingItem = await ownerClient.PostAsJsonAsync(
            Endpoint,
            SpecificRecordsRequest(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, missingItem.StatusCode);

        using var unknownType = await ownerClient.PostAsJsonAsync(
            Endpoint,
            new
            {
                scope = "SpecificRecords",
                idempotencyKey = Guid.NewGuid(),
                items = new[] { new { resourceType = "unknown_record", resourceId = ownerEpisode } }
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownType.StatusCode);
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task ManagedRelationshipIdentifiersGrantNoCreationOrListingAuthority()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var subjectClient = factory.CreateApiClient();
        using var managerClient = factory.CreateApiClient();
        var subject = await AuthenticateAsync(factory, subjectClient, "authority-subject");
        var manager = await AuthenticateAsync(factory, managerClient, "authority-manager");
        SetBearer(subjectClient, subject.AccessToken);
        SetBearer(managerClient, manager.AccessToken);
        var relationship = CareRelationship.Create(
            EntityId.From(manager.Account.ProfileId),
            EntityId.From(subject.Account.ProfileId),
            CareRelationshipType.Caregiver,
            EntityId.From(manager.Account.AccountId),
            AuthorizationAttestation.Create("phase-11.2-test", Now.AddMinutes(-2)),
            Now.AddMinutes(-2));
        await using (var dbContext = CreateDbContext())
        {
            dbContext.CareRelationships.Add(relationship);
            await dbContext.SaveChangesAsync();
        }

        using var activeAttempt = await managerClient.PostAsJsonAsync(
            Endpoint,
            new
            {
                scope = "FullProfile",
                idempotencyKey = Guid.NewGuid(),
                patientId = subject.Account.ProfileId
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, activeAttempt.StatusCode);

        await using (var dbContext = CreateDbContext())
        {
            var tracked = await dbContext.CareRelationships.SingleAsync(
                value => value.Id == relationship.Id);
            tracked.Revoke(EntityId.From(manager.Account.AccountId), Now.AddMinutes(-1));
            await dbContext.SaveChangesAsync();
        }

        using var revokedAttempt = await managerClient.PostAsJsonAsync(
            Endpoint,
            new
            {
                scope = "FullProfile",
                idempotencyKey = Guid.NewGuid(),
                beeexyId = subject.Account.BeeexyId
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, revokedAttempt.StatusCode);

        using var subjectCreate = await subjectClient.PostAsJsonAsync(
            Endpoint,
            ShareRequest("FullProfile", Guid.NewGuid()));
        using var managerCreate = await managerClient.PostAsJsonAsync(
            Endpoint,
            ShareRequest("FullProfile", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, subjectCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, managerCreate.StatusCode);

        using var subjectList = await subjectClient.GetAsync(Endpoint);
        using var managerList = await managerClient.GetAsync(Endpoint);
        var subjectBody = await subjectList.Content.ReadAsStringAsync();
        var managerBody = await managerList.Content.ReadAsStringAsync();
        using var subjectDocument = JsonDocument.Parse(subjectBody);
        using var managerDocument = JsonDocument.Parse(managerBody);
        var subjectIds = subjectDocument.RootElement.GetProperty("shares")
            .EnumerateArray().Select(value => value.GetProperty("shareGrantId").GetGuid()).ToArray();
        var managerIds = managerDocument.RootElement.GetProperty("shares")
            .EnumerateArray().Select(value => value.GetProperty("shareGrantId").GetGuid()).ToArray();
        Assert.Empty(subjectIds.Intersect(managerIds));

        using var idorList = await managerClient.GetAsync(
            $"{Endpoint}?patientId={subject.Account.ProfileId:D}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, idorList.StatusCode);
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task Listing_IsDeterministicAndTruthfullyMapsActiveRevokedAndExpired()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "list-status");
        SetBearer(client, authentication.AccessToken);
        var patientId = EntityId.From(authentication.Account.ProfileId);
        var accountId = EntityId.From(authentication.Account.AccountId);
        var active = CreatePersistedGrant(patientId, accountId, Now.AddMinutes(-10), Now.AddHours(1), 'c');
        var revoked = CreatePersistedGrant(patientId, accountId, Now.AddMinutes(-20), Now.AddHours(1), 'd');
        revoked.Revoke(accountId, Now.AddMinutes(-1));
        var expired = CreatePersistedGrant(patientId, accountId, Now.AddMinutes(-30), Now.AddMinutes(-5), 'e');
        await using (var dbContext = CreateDbContext())
        {
            dbContext.ShareGrants.AddRange(active, revoked, expired);
            await dbContext.SaveChangesAsync();
        }

        using var response = await client.GetAsync(Endpoint);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var shares = document.RootElement.GetProperty("shares").EnumerateArray().ToArray();

        Assert.Equal(
            new[] { active.Id.Value, revoked.Id.Value, expired.Id.Value },
            shares.Select(value => value.GetProperty("shareGrantId").GetGuid()));
        Assert.Equal(
            new[] { "Active", "Revoked", "Expired" },
            shares.Select(value => value.GetProperty("status").GetString()));
        Assert.True(shares[1].TryGetProperty("revokedAt", out _));
        Assert.False(shares[0].TryGetProperty("revokedAt", out _));
        Assert.False(shares[2].TryGetProperty("revokedAt", out _));
    }

    [Fact]
    [Trait("Category", "Phase112")]
    public async Task AuthenticationDisabledAccountAndPrimaryInvariantUseExistingSafeBehavior()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var anonymous = factory.CreateApiClient();
        using var missingPost = await anonymous.PostAsJsonAsync(
            Endpoint,
            ShareRequest("FullProfile", Guid.NewGuid()));
        using var missingGet = await anonymous.GetAsync(Endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, missingPost.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, missingGet.StatusCode);
        SetBearer(anonymous, "not-a-valid-token");
        using var malformedPost = await anonymous.PostAsJsonAsync(
            Endpoint,
            ShareRequest("FullProfile", Guid.NewGuid()));
        using var malformedGet = await anonymous.GetAsync(Endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, malformedPost.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, malformedGet.StatusCode);

        var expiredToken = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
            EntityId.New(),
            EntityId.New(),
            Now.AddHours(-1)).Value;
        SetBearer(anonymous, expiredToken);
        using var expiredPost = await anonymous.PostAsJsonAsync(
            Endpoint,
            ShareRequest("FullProfile", Guid.NewGuid()));
        using var expiredGet = await anonymous.GetAsync(Endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, expiredPost.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, expiredGet.StatusCode);

        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "disabled");
        SetBearer(client, authentication.AccessToken);
        await using (var dbContext = CreateDbContext())
        {
            var accountId = EntityId.From(authentication.Account.AccountId);
            var account = await dbContext.Accounts.SingleAsync(value => value.Id == accountId);
            account.Disable(Now.AddMinutes(1));
            await dbContext.SaveChangesAsync();
        }

        using var disabledPost = await client.PostAsJsonAsync(
            Endpoint,
            ShareRequest("FullProfile", Guid.NewGuid()));
        using var disabledGet = await client.GetAsync(Endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, disabledPost.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, disabledGet.StatusCode);
        Assert.DoesNotContain(
            "disabled",
            await disabledPost.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        using var invariantClient = factory.CreateApiClient();
        var invariant = await AuthenticateAsync(factory, invariantClient, "missing-primary");
        SetBearer(invariantClient, invariant.AccessToken);
        await using (var dbContext = CreateDbContext())
        {
            var profileId = EntityId.From(invariant.Account.ProfileId);
            var profile = await dbContext.PatientProfiles.SingleAsync(
                value => value.Id == profileId);
            dbContext.PatientProfiles.Remove(profile);
            await dbContext.SaveChangesAsync();
        }

        using var invariantPost = await invariantClient.PostAsJsonAsync(
            Endpoint,
            ShareRequest("FullProfile", Guid.NewGuid()));
        using var invariantGet = await invariantClient.GetAsync(Endpoint);
        var invariantBody = await invariantGet.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, invariantPost.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, invariantGet.StatusCode);
        Assert.DoesNotContain("primary-profile-count", invariantBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", invariantBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Phase112")]
    [Trait("Category", "Phase113")]
    public async Task OpenApi_PreservesTwoBearerSharingOperationsAndNoInternalSchemas()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(55, paths.EnumerateObject().Count());
        var sharing = paths.GetProperty(Endpoint);
        var operations = sharing.EnumerateObject()
            .Where(value => value.Name is "get" or "post")
            .ToArray();
        Assert.Equal(2, operations.Length);
        Assert.All(operations, operation =>
        {
            var security = operation.Value.GetProperty("security");
            Assert.Single(security.EnumerateArray());
            Assert.True(security[0].TryGetProperty("Bearer", out _));
        });
        AssertResponseCodes(sharing.GetProperty("post"),
            "200", "201", "400", "401", "404", "409", "422", "500");
        AssertResponseCodes(sharing.GetProperty("get"),
            "200", "401", "404", "422", "500");
        Assert.DoesNotContain(paths.EnumerateObject(), path =>
            (path.Name.StartsWith("/api/v1/shared-access", StringComparison.Ordinal) &&
             path.Name != "/api/v1/shared-access/exchange") ||
            (path.Name.StartsWith("/api/v1/shares", StringComparison.Ordinal) &&
             path.Name != Endpoint) ||
            path.Name.StartsWith("/api/v1/exports", StringComparison.Ordinal));

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var schema in schemas.EnumerateObject().Where(value =>
                     value.Name.Contains("Share", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.DoesNotContain("capabilityHash", schema.Value.GetRawText(),
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("creatorAccount", schema.Value.GetRawText(),
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("storage", schema.Value.GetRawText(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private BeeexyApiFactory CreateFactory(InMemoryLoggerProvider? logger = null) => new(
        postgres.ConnectionString,
        loggerProvider: logger,
        configurationOverrides: new Dictionary<string, string?>
        {
            ["Sharing:PublicShareBaseUrl"] = PublicBaseUrl
        },
        configureServices: services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(new FixedClock(Now));
        });

    private async Task<AuthenticationResult> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client,
        string prefix)
    {
        var email = $"share-{prefix}-{Guid.NewGuid():N}@example.com";
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

    private async Task<Guid> SeedEpisodeAsync(Guid patientProfileId, string prefix)
    {
        var createdAt = Now.AddHours(-2);
        var questionnaire = QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create($"share-{prefix}-{Guid.NewGuid():N}"),
            DefinitionVersion.Create("phase-11.2-test"),
            DefinitionHash.FromHash(new string('a', 64)),
            createdAt,
            createdAt);
        var ruleSet = ClinicalRuleSetVersion.ImportApproved(
            RuleSetCode.Create($"share-{prefix}-{Guid.NewGuid():N}"),
            DefinitionVersion.Create("phase-11.2-test"),
            DefinitionHash.FromHash(new string('b', 64)),
            createdAt,
            createdAt);
        var session = PreTriageSession.CreateForPatient(
            EntityId.From(patientProfileId),
            questionnaire.Id,
            Now.AddHours(1),
            createdAt);
        var episode = PreTriageEpisode.CreateFrom(session, ruleSet.Id, Now.AddHours(-1));
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(questionnaire, ruleSet, session, episode);
        await dbContext.SaveChangesAsync();
        return episode.Id.Value;
    }

    private static object ShareRequest(string scope, Guid key, int? lifetimeMinutes = null) =>
        new { scope, lifetimeMinutes, idempotencyKey = key };

    private static object SpecificRecordsRequest(Guid key, Guid episodeId) => new
    {
        scope = "SpecificRecords",
        idempotencyKey = key,
        items = new[]
        {
            new
            {
                resourceType = SupportedShareResourceTypes.PreTriageEpisode,
                resourceId = episodeId
            }
        }
    };

    private static ShareGrant CreatePersistedGrant(
        EntityId patientId,
        EntityId accountId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        char hashCharacter) => ShareGrant.Create(
        patientId,
        accountId,
        EntityId.New(),
        ShareRequestFingerprint.Create(new string(hashCharacter, 64)),
        ShareScope.FullProfile,
        TokenHash.FromHash("sha256:" + new string(hashCharacter, 64)),
        createdAt,
        expiresAt);

    private async Task<int> CountPatientGrantsAsync(Guid patientProfileId)
    {
        await using var dbContext = CreateDbContext();
        var id = EntityId.From(patientProfileId);
        return await dbContext.ShareGrants.CountAsync(value => value.PatientProfileId == id);
    }

    private static void AssertResponseCodes(JsonElement operation, params string[] expected)
    {
        var actual = operation.GetProperty("responses").EnumerateObject()
            .Select(value => value.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static DateTimeOffset Normalize(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed record AuthenticationResult(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);
}
