using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class CareRelationshipRevocationEndpointTests(
    PostgreSqlContainerFixture postgres)
{
    private const string RelationshipsEndpoint = "/api/v1/care-relationships";
    private const string PatientsEndpoint = "/api/v1/patients";

    [Fact]
    public async Task OwningManager_RevokesOnceAndRepeatedDeletePreservesMetadataAndAudit()
    {
        await EnsureMigratedAsync();
        var logger = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: logger);
        using var client = factory.CreateApiClient();
        var manager = await AuthenticateAsync(factory, client);
        SetBearer(client, manager.AccessToken);
        var created = await CreateManagedPatientAsync(client);
        var before = DateTimeOffset.UtcNow;

        using var first = await client.DeleteAsync(RelationshipEndpoint(created.Relationship.Id));
        var after = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var firstState = await LoadRelationshipAsync(created.Relationship.Id);
        Assert.Equal(CareRelationshipStatus.Revoked, firstState.Status);
        Assert.Equal(EntityId.From(manager.Account.AccountId), firstState.RevokedByAccountId);
        Assert.NotNull(firstState.RevokedAt);
        Assert.InRange(firstState.RevokedAt.Value, before, after);
        Assert.Equal(firstState.RevokedAt, firstState.UpdatedAt);

        using var repeat = await client.DeleteAsync(RelationshipEndpoint(created.Relationship.Id));
        Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);
        var repeatedState = await LoadRelationshipAsync(created.Relationship.Id);
        Assert.Equal(firstState.RevokedAt, repeatedState.RevokedAt);
        Assert.Equal(firstState.RevokedByAccountId, repeatedState.RevokedByAccountId);
        Assert.Single(logger.Messages, message =>
            message.Contains("Care relationship revocation succeeded", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message =>
            message.Contains("Authorization", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("attestation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnknownAndForeignRelationship_ReturnEquivalentConcealedNotFound()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var actorClient = factory.CreateApiClient();
        using var ownerClient = factory.CreateApiClient();
        var actor = await AuthenticateAsync(factory, actorClient);
        var owner = await AuthenticateAsync(factory, ownerClient);
        SetBearer(actorClient, actor.AccessToken);
        SetBearer(ownerClient, owner.AccessToken);
        var foreign = await CreateManagedPatientAsync(ownerClient);

        using var unknownResponse = await actorClient.DeleteAsync(
            RelationshipEndpoint(Guid.NewGuid()));
        using var foreignResponse = await actorClient.DeleteAsync(
            RelationshipEndpoint(foreign.Relationship.Id));
        var unknown = await ReadProblemAsync(unknownResponse);
        var foreignProblem = await ReadProblemAsync(foreignResponse);

        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        AssertPubliclyEquivalent(unknown, foreignProblem);
        var relationship = await LoadRelationshipAsync(foreign.Relationship.Id);
        Assert.Equal(CareRelationshipStatus.Active, relationship.Status);
        Assert.Null(relationship.RevokedAt);
    }

    [Fact]
    public async Task MissingAndInvalidBearer_ReturnUnauthorized()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var endpoint = RelationshipEndpoint(Guid.NewGuid());

        using var missing = await client.DeleteAsync(endpoint);
        SetBearer(client, "not-a-valid-jwt");
        using var invalid = await client.DeleteAsync(endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [Fact]
    public async Task MalformedRelationshipUuid_UsesConcealedRouteNotFoundConvention()
    {
        using var context = await CreateAuthenticatedContextAsync();

        using var response = await context.Client.DeleteAsync(
            $"{RelationshipsEndpoint}/not-a-uuid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DisabledAccount_ReturnsGenericUnauthorizedWithoutRevocation()
    {
        using var context = await CreateAuthenticatedContextAsync();
        var created = await CreateManagedPatientAsync(context.Client);
        await using (var dbContext = CreateDbContext())
        {
            var accountId = EntityId.From(context.Authentication.Account.AccountId);
            var account = await dbContext.Accounts.SingleAsync(value => value.Id == accountId);
            account.Disable(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        using var response = await context.Client.DeleteAsync(
            RelationshipEndpoint(created.Relationship.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("disabled", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            CareRelationshipStatus.Active,
            (await LoadRelationshipAsync(created.Relationship.Id)).Status);
    }

    [Fact]
    public async Task MissingPrimaryProfile_ReturnsSafeServerFailureWithoutMutation()
    {
        using var context = await CreateAuthenticatedContextAsync();
        await using (var dbContext = CreateDbContext())
        {
            var profileId = EntityId.From(context.Authentication.Account.ProfileId);
            var profile = await dbContext.PatientProfiles.SingleAsync(value => value.Id == profileId);
            dbContext.PatientProfiles.Remove(profile);
            await dbContext.SaveChangesAsync();
        }

        using var response = await context.Client.DeleteAsync(
            RelationshipEndpoint(Guid.NewGuid()));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("primary-profile-count", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultipleManagers_RevokingOneRelationshipLeavesOtherManagerAuthorized()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var clientA = factory.CreateApiClient();
        using var clientB = factory.CreateApiClient();
        var managerA = await AuthenticateAsync(factory, clientA);
        var managerB = await AuthenticateAsync(factory, clientB);
        SetBearer(clientA, managerA.AccessToken);
        SetBearer(clientB, managerB.AccessToken);
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var subject = CreateUnownedProfile(createdAt);
        var relationshipA = CreateRelationship(managerA, subject.Id, createdAt);
        var relationshipB = CreateRelationship(managerB, subject.Id, createdAt.AddSeconds(1));
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(subject, relationshipA, relationshipB);
            await dbContext.SaveChangesAsync();
        }

        using var revoke = await clientA.DeleteAsync(
            RelationshipEndpoint(relationshipA.Id.Value));
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var patientsA = await GetPatientsAsync(clientA);
        var patientsB = await GetPatientsAsync(clientB);
        Assert.DoesNotContain(patientsA.Patients, value => value.ProfileId == subject.Id.Value);
        Assert.Contains(patientsB.Patients, value => value.ProfileId == subject.Id.Value);
        using var detailA = await clientA.GetAsync(PatientEndpoint(subject.Id.Value));
        using var detailB = await clientB.GetAsync(PatientEndpoint(subject.Id.Value));
        Assert.Equal(HttpStatusCode.NotFound, detailA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detailB.StatusCode);

        var relationshipsA = await GetRelationshipsAsync(clientA);
        var relationshipsB = await GetRelationshipsAsync(clientB);
        Assert.Equal("Revoked", Assert.Single(
            relationshipsA.Relationships,
            value => value.Id == relationshipA.Id.Value).Status);
        Assert.Equal("Active", Assert.Single(
            relationshipsB.Relationships,
            value => value.Id == relationshipB.Id.Value).Status);

        await using var verification = CreateDbContext();
        Assert.True(await verification.PatientProfiles.AnyAsync(value => value.Id == subject.Id));
        Assert.Equal(2, await verification.CareRelationships.CountAsync(value =>
            value.SubjectProfileId == subject.Id));
    }

    [Fact]
    public async Task ConcurrentDeleteRequests_BothSucceedWithOneStableRevocation()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var setupClient = factory.CreateApiClient();
        using var firstClient = factory.CreateApiClient();
        using var secondClient = factory.CreateApiClient();
        var manager = await AuthenticateAsync(factory, setupClient);
        SetBearer(setupClient, manager.AccessToken);
        SetBearer(firstClient, manager.AccessToken);
        SetBearer(secondClient, manager.AccessToken);
        var created = await CreateManagedPatientAsync(setupClient);
        var endpoint = RelationshipEndpoint(created.Relationship.Id);

        var responses = await Task.WhenAll(
            firstClient.DeleteAsync(endpoint),
            secondClient.DeleteAsync(endpoint));
        try
        {
            Assert.All(responses, response =>
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var dbContext = CreateDbContext();
        var relationshipId = EntityId.From(created.Relationship.Id);
        var rows = await dbContext.CareRelationships.AsNoTracking()
            .Where(value => value.Id == relationshipId)
            .ToArrayAsync();
        var persisted = Assert.Single(rows);
        Assert.Equal(CareRelationshipStatus.Revoked, persisted.Status);
        Assert.NotNull(persisted.RevokedAt);
        Assert.Equal(persisted.RevokedAt, persisted.UpdatedAt);
        Assert.Equal(EntityId.From(manager.Account.AccountId), persisted.RevokedByAccountId);
    }

    [Fact]
    public async Task CreateListReadRevokeDeniedRepeat_PreservesSubjectAndHistory()
    {
        using var context = await CreateAuthenticatedContextAsync();
        var created = await CreateManagedPatientAsync(context.Client);

        var beforePatients = await GetPatientsAsync(context.Client);
        Assert.Contains(beforePatients.Patients, value =>
            value.ProfileId == created.Patient.ProfileId);
        using var beforeDetail = await context.Client.GetAsync(
            PatientEndpoint(created.Patient.ProfileId));
        Assert.Equal(HttpStatusCode.OK, beforeDetail.StatusCode);

        using var delete = await context.Client.DeleteAsync(
            RelationshipEndpoint(created.Relationship.Id));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var afterPatients = await GetPatientsAsync(context.Client);
        Assert.DoesNotContain(afterPatients.Patients, value =>
            value.ProfileId == created.Patient.ProfileId);
        var relationships = await GetRelationshipsAsync(context.Client);
        var historical = Assert.Single(
            relationships.Relationships,
            value => value.Id == created.Relationship.Id);
        Assert.Equal("Revoked", historical.Status);
        Assert.NotNull(historical.RevokedAt);

        using var deniedRead = await context.Client.GetAsync(
            PatientEndpoint(created.Patient.ProfileId));
        using var deniedPatch = await context.Client.PatchAsJsonAsync(
            PatientEndpoint(created.Patient.ProfileId),
            new { });
        using var repeat = await context.Client.DeleteAsync(
            RelationshipEndpoint(created.Relationship.Id));
        Assert.Equal(HttpStatusCode.NotFound, deniedRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedPatch.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);

        await using var dbContext = CreateDbContext();
        var subjectId = EntityId.From(created.Patient.ProfileId);
        var persistedSubject = await dbContext.PatientProfiles.AsNoTracking()
            .SingleAsync(value => value.Id == subjectId);
        var persistedRelationship = await dbContext.CareRelationships.AsNoTracking()
            .SingleAsync(value => value.Id == EntityId.From(created.Relationship.Id));
        Assert.Equal(created.Patient.BeeexyId, persistedSubject.BeeexyId.Value);
        Assert.Null(persistedSubject.AccountId);
        Assert.Equal(CareRelationshipStatus.Revoked, persistedRelationship.Status);
        Assert.Equal(subjectId, persistedRelationship.SubjectProfileId);
    }

    [Fact]
    public async Task OpenApi_DocumentsOnlyTheIntendedAuthenticatedDeleteOperation()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var relationshipDetail = paths.GetProperty(
            "/api/v1/care-relationships/{id}");
        var operation = relationshipDetail.GetProperty("delete");
        var parameter = Assert.Single(operation.GetProperty("parameters").EnumerateArray());

        Assert.Equal(46, paths.EnumerateObject().Count());
        Assert.Single(relationshipDetail.EnumerateObject());
        Assert.Equal("id", parameter.GetProperty("name").GetString());
        Assert.Equal("path", parameter.GetProperty("in").GetString());
        Assert.Equal("uuid", parameter.GetProperty("schema").GetProperty("format").GetString());
        foreach (var status in new[] { "204", "401", "404", "500" })
        {
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        }
        var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
        Assert.True(security.TryGetProperty("Bearer", out _));
        Assert.False(operation.TryGetProperty("requestBody", out _));
        Assert.True(paths.GetProperty(RelationshipsEndpoint).TryGetProperty("get", out _));
        Assert.True(paths.GetProperty(RelationshipsEndpoint).TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/v1/patients/{patientId}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/patients/{patientId}").TryGetProperty("patch", out _));
    }

    private async Task<AuthenticatedContext> CreateAuthenticatedContextAsync()
    {
        await EnsureMigratedAsync();
        var factory = new BeeexyApiFactory(postgres.ConnectionString);
        var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client);
        SetBearer(client, authentication.AccessToken);
        return new AuthenticatedContext(factory, client, authentication);
    }

    private static async Task<AuthenticationResult> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client)
    {
        var email = $"care-revoke-{Guid.NewGuid():N}@example.com";
        using var challengeResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/email/challenges",
            new { email });
        Assert.Equal(HttpStatusCode.Accepted, challengeResponse.StatusCode);
        var message = Assert.Single(
            factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>().Messages,
            value => value.Recipient.Value == email);
        using var verificationResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/email/verify",
            new { email, code = message.OneTimeCode });
        verificationResponse.EnsureSuccessStatusCode();
        var result = await verificationResponse.Content
            .ReadFromJsonAsync<AuthenticationResult>();
        return Assert.IsType<AuthenticationResult>(result);
    }

    private static async Task<CreateResponse> CreateManagedPatientAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            RelationshipsEndpoint,
            new
            {
                relationshipType = "Caregiver",
                attestationVersion = "phase-3.7-test",
                attestationAccepted = true,
                patient = new
                {
                    firstName = "Maria",
                    lastName = "Arias",
                    dateOfBirth = "2012-05-12",
                    sexAssignedAtBirth = "Female",
                    state = "NY"
                }
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreateResponse>();
        return Assert.IsType<CreateResponse>(result);
    }

    private static async Task<AccessiblePatientsResponse> GetPatientsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(PatientsEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AccessiblePatientsResponse>();
        return Assert.IsType<AccessiblePatientsResponse>(result);
    }

    private static async Task<CareRelationshipsResponse> GetRelationshipsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(RelationshipsEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CareRelationshipsResponse>();
        return Assert.IsType<CareRelationshipsResponse>(result);
    }

    private async Task<CareRelationship> LoadRelationshipAsync(Guid relationshipId)
    {
        await using var dbContext = CreateDbContext();
        var id = EntityId.From(relationshipId);
        return await dbContext.CareRelationships.AsNoTracking()
            .SingleAsync(value => value.Id == id);
    }

    private static PatientProfile CreateUnownedProfile(DateTimeOffset createdAt) =>
        PatientProfile.Create(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            createdAt);

    private static CareRelationship CreateRelationship(
        AuthenticationResult manager,
        EntityId subjectProfileId,
        DateTimeOffset createdAt) =>
        CareRelationship.Create(
            EntityId.From(manager.Account.ProfileId),
            subjectProfileId,
            CareRelationshipType.Caregiver,
            EntityId.From(manager.Account.AccountId),
            AuthorizationAttestation.Create("phase-3.7-test", createdAt),
            createdAt);

    private static string RelationshipEndpoint(Guid relationshipId) =>
        $"{RelationshipsEndpoint}/{relationshipId:D}";

    private static string PatientEndpoint(Guid patientId) =>
        $"{PatientsEndpoint}/{patientId:D}";

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<ProblemResponse> ReadProblemAsync(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        return Assert.IsType<ProblemResponse>(result);
    }

    private static void AssertPubliclyEquivalent(
        ProblemResponse expected,
        ProblemResponse actual)
    {
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Detail, actual.Detail);
    }

    private BeeexyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options;
        return new BeeexyDbContext(options);
    }

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private sealed class AuthenticatedContext(
        BeeexyApiFactory factory,
        HttpClient client,
        AuthenticationResult authentication) : IDisposable
    {
        public HttpClient Client { get; } = client;

        public AuthenticationResult Authentication { get; } = authentication;

        public void Dispose()
        {
            Client.Dispose();
            factory.Dispose();
        }
    }

    private sealed record AuthenticationResult(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);

    private sealed record CreateResponse(
        CreatedRelationshipResponse Relationship,
        CreatedPatientResponse Patient);

    private sealed record CreatedRelationshipResponse(Guid Id);

    private sealed record CreatedPatientResponse(Guid ProfileId, string BeeexyId);

    private sealed record AccessiblePatientsResponse(
        IReadOnlyList<AccessiblePatientResponse> Patients);

    private sealed record AccessiblePatientResponse(Guid ProfileId);

    private sealed record CareRelationshipsResponse(
        IReadOnlyList<CareRelationshipResponse> Relationships);

    private sealed record CareRelationshipResponse(
        Guid Id,
        string Status,
        DateTimeOffset? RevokedAt);

    private sealed record ProblemResponse(
        int Status,
        string Title,
        string? Type,
        string? Detail);
}
