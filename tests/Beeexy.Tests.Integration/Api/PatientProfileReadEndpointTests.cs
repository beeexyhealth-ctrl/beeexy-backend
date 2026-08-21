using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class PatientProfileReadEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string PatientsEndpoint = "/api/v1/patients";
    private const string RelationshipsEndpoint = "/api/v1/care-relationships";

    [Fact]
    public async Task OwnPrimaryProfile_ReturnsApprovedDetailAndPreservesPatientsMe()
    {
        using var context = await CreateAuthenticatedContextAsync();

        using var detailResponse = await context.Client.GetAsync(
            PatientEndpoint(context.Authentication.Account.ProfileId));
        using var detailDocument = JsonDocument.Parse(
            await detailResponse.Content.ReadAsStringAsync());
        using var meResponse = await context.Client.GetAsync("/api/v1/patients/me");
        var detail = await detailResponse.Content.ReadFromJsonAsync<PatientResponse>();
        var me = await meResponse.Content.ReadFromJsonAsync<PrimaryPatientResponse>();

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.NotNull(detail);
        Assert.NotNull(me);
        Assert.Equal(context.Authentication.Account.ProfileId, detail.ProfileId);
        Assert.Equal(context.Authentication.Account.BeeexyId, detail.BeeexyId);
        Assert.Equal(me.ProfileId, detail.ProfileId);
        Assert.Equal(me.BeeexyId, detail.BeeexyId);
        Assert.Equal(2, detailDocument.RootElement.EnumerateObject().Count());
        Assert.False(detailDocument.RootElement.TryGetProperty("preferences", out _));
        Assert.False(detailDocument.RootElement.TryGetProperty("authorizationReason", out _));
    }

    [Fact]
    public async Task ActiveManagedProfile_ReturnsPatientWithoutManagerPreferences()
    {
        using var context = await CreateAuthenticatedContextAsync();
        var seeded = await SeedRelationshipAsync(context.Authentication);

        using var response = await context.Client.GetAsync(PatientEndpoint(seeded.Subject.Id.Value));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var patient = await response.Content.ReadFromJsonAsync<PatientResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(patient);
        Assert.Equal(seeded.Subject.Id.Value, patient.ProfileId);
        Assert.Equal(seeded.Subject.BeeexyId.Value, patient.BeeexyId);
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
        Assert.False(document.RootElement.TryGetProperty("preferences", out _));
        Assert.False(document.RootElement.TryGetProperty("version", out _));
    }

    [Fact]
    public async Task UnknownAndUnauthorizedRealPatient_ReturnEquivalentConcealedNotFound()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var accountA = await AuthenticateAsync(factory, client);
        var accountB = await AuthenticateAsync(factory, client);
        SetBearer(client, accountA.AccessToken);

        using var unknownResponse = await client.GetAsync(PatientEndpoint(Guid.NewGuid()));
        using var unauthorizedResponse = await client.GetAsync(
            PatientEndpoint(accountB.Account.ProfileId));
        var unknown = await ReadProblemAsync(unknownResponse);
        var unauthorized = await ReadProblemAsync(unauthorizedResponse);

        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedResponse.StatusCode);
        Assert.Equal(unknown.Status, unauthorized.Status);
        Assert.Equal(unknown.Title, unauthorized.Title);
        Assert.Equal(unknown.Type, unauthorized.Type);
        Assert.Equal(unknown.Detail, unauthorized.Detail);
    }

    [Fact]
    public async Task RevokedRelationship_ReturnsNotFoundAndPreservesPatientAndHistory()
    {
        using var context = await CreateAuthenticatedContextAsync();
        var seeded = await SeedRelationshipAsync(context.Authentication, revoked: true);

        using var response = await context.Client.GetAsync(PatientEndpoint(seeded.Subject.Id.Value));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var dbContext = CreateDbContext();
        Assert.True(await dbContext.PatientProfiles.AnyAsync(
            profile => profile.Id == seeded.Subject.Id));
        var relationship = await dbContext.CareRelationships.AsNoTracking().SingleAsync(
            candidate => candidate.Id == seeded.Relationship.Id);
        Assert.Equal(CareRelationshipStatus.Revoked, relationship.Status);
    }

    [Fact]
    public async Task MultipleManagers_AccessIndependentlyAndSingleRevocationIsIsolated()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var clientA = factory.CreateApiClient();
        using var clientB = factory.CreateApiClient();
        var accountA = await AuthenticateAsync(factory, clientA);
        var accountB = await AuthenticateAsync(factory, clientB);
        SetBearer(clientA, accountA.AccessToken);
        SetBearer(clientB, accountB.AccessToken);
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = CreateManagedProfile(createdAt);
        var relationshipA = CreateRelationship(accountA, subject.Id, createdAt);
        var relationshipB = CreateRelationship(accountB, subject.Id, createdAt.AddMinutes(1));
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(subject, relationshipA, relationshipB);
            await dbContext.SaveChangesAsync();
        }

        using var aBefore = await clientA.GetAsync(PatientEndpoint(subject.Id.Value));
        using var bBefore = await clientB.GetAsync(PatientEndpoint(subject.Id.Value));
        Assert.Equal(HttpStatusCode.OK, aBefore.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bBefore.StatusCode);

        await using (var dbContext = CreateDbContext())
        {
            var persistedA = await dbContext.CareRelationships.SingleAsync(
                relationship => relationship.Id == relationshipA.Id);
            persistedA.Revoke(EntityId.From(accountA.Account.AccountId), DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        using var aAfter = await clientA.GetAsync(PatientEndpoint(subject.Id.Value));
        using var bAfter = await clientB.GetAsync(PatientEndpoint(subject.Id.Value));
        Assert.Equal(HttpStatusCode.NotFound, aAfter.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bAfter.StatusCode);
        await using var verification = CreateDbContext();
        Assert.True(await verification.PatientProfiles.AnyAsync(profile => profile.Id == subject.Id));
        Assert.Equal(
            2,
            await verification.CareRelationships.CountAsync(
                relationship => relationship.SubjectProfileId == subject.Id));
    }

    [Fact]
    public async Task RelationshipSubject_DoesNotGainReverseAccess()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var subjectClient = factory.CreateApiClient();
        using var managerClient = factory.CreateApiClient();
        var subject = await AuthenticateAsync(factory, subjectClient);
        var manager = await AuthenticateAsync(factory, managerClient);
        var relationship = CreateRelationship(
            manager,
            EntityId.From(subject.Account.ProfileId),
            DateTimeOffset.UtcNow.AddMinutes(-1));
        await using (var dbContext = CreateDbContext())
        {
            dbContext.CareRelationships.Add(relationship);
            await dbContext.SaveChangesAsync();
        }
        SetBearer(subjectClient, subject.AccessToken);

        using var response = await subjectClient.GetAsync(
            PatientEndpoint(manager.Account.ProfileId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CrossAccountUuidAndBeeexyId_DoNotGrantAuthority()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var clientA = factory.CreateApiClient();
        using var clientB = factory.CreateApiClient();
        var accountA = await AuthenticateAsync(factory, clientA);
        var accountB = await AuthenticateAsync(factory, clientB);
        SetBearer(clientA, accountA.AccessToken);
        SetBearer(clientB, accountB.AccessToken);
        var managedByB = await SeedRelationshipAsync(accountB);

        using var primaryResponse = await clientA.GetAsync(
            PatientEndpoint(accountB.Account.ProfileId));
        using var managedResponse = await clientA.GetAsync(
            PatientEndpoint(managedByB.Subject.Id.Value));
        using var beeexyResponse = await clientA.GetAsync(
            $"{PatientsEndpoint}/{accountB.Account.BeeexyId}");

        Assert.Equal(HttpStatusCode.NotFound, primaryResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, managedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, beeexyResponse.StatusCode);
    }

    [Fact]
    public async Task MissingAndInvalidBearer_ReturnUnauthorized()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var endpoint = PatientEndpoint(Guid.NewGuid());

        using var missing = await client.GetAsync(endpoint);
        SetBearer(client, "not-a-valid-jwt");
        using var invalid = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [Fact]
    public async Task MalformedUuid_FollowsGuidRouteConstraintWithNotFound()
    {
        using var context = await CreateAuthenticatedContextAsync();

        using var response = await context.Client.GetAsync($"{PatientsEndpoint}/not-a-uuid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Phase32Creation_IsImmediatelyReadableByAuthorizedManager()
    {
        using var context = await CreateAuthenticatedContextAsync();
        using var creationResponse = await context.Client.PostAsJsonAsync(
            RelationshipsEndpoint,
            new
            {
                relationshipType = "Child",
                attestationVersion = "phase-3.5-create-read",
                attestationAccepted = true
            });
        var created = await creationResponse.Content.ReadFromJsonAsync<CreateResponse>();
        Assert.Equal(HttpStatusCode.Created, creationResponse.StatusCode);
        Assert.NotNull(created);

        using var readResponse = await context.Client.GetAsync(
            PatientEndpoint(created.Patient.ProfileId));
        var patient = await readResponse.Content.ReadFromJsonAsync<PatientResponse>();

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.NotNull(patient);
        Assert.Equal(created.Patient, patient);
    }

    [Fact]
    public async Task Phase33Listing_ContainsOnlyPatientsReadableById()
    {
        using var context = await CreateAuthenticatedContextAsync();
        var active = await SeedRelationshipAsync(context.Authentication);
        var revoked = await SeedRelationshipAsync(context.Authentication, revoked: true);

        using var listResponse = await context.Client.GetAsync(PatientsEndpoint);
        var list = await listResponse.Content.ReadFromJsonAsync<AccessiblePatientsResponse>();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(list);

        foreach (var listed in list.Patients)
        {
            using var detail = await context.Client.GetAsync(PatientEndpoint(listed.ProfileId));
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        }

        Assert.Contains(list.Patients, patient => patient.ProfileId == active.Subject.Id.Value);
        Assert.DoesNotContain(list.Patients, patient => patient.ProfileId == revoked.Subject.Id.Value);
        using var revokedDetail = await context.Client.GetAsync(
            PatientEndpoint(revoked.Subject.Id.Value));
        Assert.Equal(HttpStatusCode.NotFound, revokedDetail.StatusCode);
    }

    [Fact]
    public async Task OpenApi_DocumentsOnlyPatientDetailGetWithUuidAndConcealedNotFound()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var detailPath = paths.GetProperty("/api/v1/patients/{patientId}");
        var operation = detailPath.GetProperty("get");
        var parameter = Assert.Single(operation.GetProperty("parameters").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12, paths.EnumerateObject().Count());
        Assert.False(detailPath.TryGetProperty("patch", out _));
        Assert.Equal("patientId", parameter.GetProperty("name").GetString());
        Assert.Equal("path", parameter.GetProperty("in").GetString());
        Assert.Equal("string", parameter.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("uuid", parameter.GetProperty("schema").GetProperty("format").GetString());
        foreach (var status in new[] { "200", "401", "404", "500" })
        {
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        }
        Assert.False(paths.TryGetProperty("/api/v1/care-relationships/{id}", out _));
    }

    [Fact]
    public async Task MissingPrimaryProfile_ReturnsSafeInternalFailure()
    {
        using var context = await CreateAuthenticatedContextAsync();
        await using (var dbContext = CreateDbContext())
        {
            var profileId = EntityId.From(context.Authentication.Account.ProfileId);
            var profile = await dbContext.PatientProfiles.SingleAsync(
                candidate => candidate.Id == profileId);
            dbContext.PatientProfiles.Remove(profile);
            await dbContext.SaveChangesAsync();
        }

        using var response = await context.Client.GetAsync(PatientEndpoint(Guid.NewGuid()));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("primary-profile-count", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", body, StringComparison.OrdinalIgnoreCase);
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

    private async Task<AuthenticationResult> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client)
    {
        var email = $"patient-read-{Guid.NewGuid():N}@example.com";
        using var challengeResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/email/challenges",
            new { email });
        Assert.Equal(HttpStatusCode.Accepted, challengeResponse.StatusCode);
        var message = Assert.Single(
            factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>().Messages,
            candidate => candidate.Recipient.Value == email);
        using var verificationResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/email/verify",
            new { email, code = message.OneTimeCode });
        verificationResponse.EnsureSuccessStatusCode();
        var authentication = await verificationResponse.Content
            .ReadFromJsonAsync<AuthenticationResult>();
        return Assert.IsType<AuthenticationResult>(authentication);
    }

    private async Task<SeededRelationship> SeedRelationshipAsync(
        AuthenticationResult authentication,
        bool revoked = false)
    {
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var subject = CreateManagedProfile(createdAt);
        var relationship = CreateRelationship(authentication, subject.Id, createdAt);
        if (revoked)
        {
            relationship.Revoke(
                EntityId.From(authentication.Account.AccountId),
                createdAt.AddMinutes(1));
        }

        await using var dbContext = CreateDbContext();
        dbContext.AddRange(subject, relationship);
        await dbContext.SaveChangesAsync();
        return new SeededRelationship(subject, relationship);
    }

    private static PatientProfile CreateManagedProfile(DateTimeOffset createdAt) =>
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
            AuthorizationAttestation.Create("phase-3.5-test", createdAt),
            createdAt);

    private static string PatientEndpoint(Guid profileId) =>
        $"{PatientsEndpoint}/{profileId:D}";

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<ProblemResponse> ReadProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        return Assert.IsType<ProblemResponse>(problem);
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

    private sealed record PatientResponse(Guid ProfileId, string BeeexyId);

    private sealed record PrimaryPatientResponse(
        Guid ProfileId,
        string BeeexyId,
        JsonElement Preferences,
        long Version);

    private sealed record ProblemResponse(
        int Status,
        string Title,
        string? Type,
        string? Detail);

    private sealed record CreateResponse(
        JsonElement Relationship,
        PatientResponse Patient);

    private sealed record AccessiblePatientsResponse(
        IReadOnlyList<AccessiblePatientResponse> Patients);

    private sealed record AccessiblePatientResponse(
        Guid ProfileId,
        string BeeexyId,
        string AccessType,
        JsonElement? Relationship);

    private sealed record SeededRelationship(
        PatientProfile Subject,
        CareRelationship Relationship);
}
