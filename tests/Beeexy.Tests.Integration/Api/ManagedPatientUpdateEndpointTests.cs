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
public sealed class ManagedPatientUpdateEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string PatientsEndpoint = "/api/v1/patients";
    private const string RelationshipsEndpoint = "/api/v1/care-relationships";

    [Fact]
    public async Task PrimaryTarget_IsAuthorizedBeforeConservativeNoFieldsValidation()
    {
        using var context = await CreateAuthenticatedContextAsync();
        var profileId = EntityId.From(context.Authentication.Account.ProfileId);
        var accountId = EntityId.From(context.Authentication.Account.AccountId);
        await using var beforeContext = CreateDbContext();
        var beforeProfile = await beforeContext.PatientProfiles.AsNoTracking().SingleAsync(
            profile => profile.Id == profileId);
        var beforePreference = await beforeContext.UserPreferences.AsNoTracking().SingleAsync(
            preference => preference.AccountId == accountId);

        using var response = await context.Client.PatchAsJsonAsync(
            PatientEndpoint(profileId.Value),
            new { });
        var problem = await ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("patient.no_mutable_fields", problem.ErrorCode);
        await using var afterContext = CreateDbContext();
        var afterProfile = await afterContext.PatientProfiles.AsNoTracking().SingleAsync(
            profile => profile.Id == profileId);
        var afterPreference = await afterContext.UserPreferences.AsNoTracking().SingleAsync(
            preference => preference.AccountId == accountId);
        Assert.Equal(beforeProfile.BeeexyId, afterProfile.BeeexyId);
        Assert.Equal(beforeProfile.UpdatedAt, afterProfile.UpdatedAt);
        Assert.Equal(beforePreference.TimeZone, afterPreference.TimeZone);
        Assert.Equal(beforePreference.Version, afterPreference.Version);
    }

    [Fact]
    public async Task ActiveManagedTarget_IsAuthorizedButManagerPreferencesAreNotApplied()
    {
        using var context = await CreateAuthenticatedContextAsync();
        var seeded = await SeedRelationshipAsync(context.Authentication);
        var accountId = EntityId.From(context.Authentication.Account.AccountId);
        await using var beforeContext = CreateDbContext();
        var preference = await beforeContext.UserPreferences.AsNoTracking().SingleAsync(
            value => value.AccountId == accountId);

        using var response = await context.Client.PatchAsJsonAsync(
            PatientEndpoint(seeded.Subject.Id.Value),
            new { timezone = "America/Lima" });
        var problem = await ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("patient.unsupported_field", problem.ErrorCode);
        await using var afterContext = CreateDbContext();
        var persistedSubject = await afterContext.PatientProfiles.AsNoTracking().SingleAsync(
            profile => profile.Id == seeded.Subject.Id);
        var persistedPreference = await afterContext.UserPreferences.AsNoTracking().SingleAsync(
            value => value.AccountId == accountId);
        Assert.Equal(seeded.Subject.BeeexyId, persistedSubject.BeeexyId);
        Assert.Null(persistedSubject.UpdatedAt);
        Assert.Equal(preference.TimeZone, persistedPreference.TimeZone);
        Assert.Equal(preference.Version, persistedPreference.Version);
    }

    [Fact]
    public async Task UnknownUnrelatedAndRevokedTargets_ReturnEquivalentConcealedNotFound()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var actor = await AuthenticateAsync(factory, client);
        var unrelated = await AuthenticateAsync(factory, client);
        SetBearer(client, actor.AccessToken);
        var revoked = await SeedRelationshipAsync(actor, revoked: true);

        using var unknownResponse = await client.PatchAsJsonAsync(
            PatientEndpoint(Guid.NewGuid()),
            new { name = "not-approved" });
        using var unrelatedResponse = await client.PatchAsJsonAsync(
            PatientEndpoint(unrelated.Account.ProfileId),
            new { name = "not-approved" });
        using var revokedResponse = await client.PatchAsJsonAsync(
            PatientEndpoint(revoked.Subject.Id.Value),
            new { name = "not-approved" });
        var unknown = await ReadProblemAsync(unknownResponse);
        var unrelatedProblem = await ReadProblemAsync(unrelatedResponse);
        var revokedProblem = await ReadProblemAsync(revokedResponse);

        foreach (var response in new[] { unknownResponse, unrelatedResponse, revokedResponse })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        AssertPubliclyEquivalent(unknown, unrelatedProblem);
        AssertPubliclyEquivalent(unknown, revokedProblem);
        await using var dbContext = CreateDbContext();
        Assert.True(await dbContext.PatientProfiles.AnyAsync(
            profile => profile.Id == revoked.Subject.Id));
        Assert.Equal(
            CareRelationshipStatus.Revoked,
            (await dbContext.CareRelationships.AsNoTracking().SingleAsync(
                relationship => relationship.Id == revoked.Relationship.Id)).Status);
    }

    [Fact]
    public async Task ImmutableAndUnsupportedFields_ReturnValidationWithoutMutation()
    {
        using var context = await CreateAuthenticatedContextAsync();
        var seeded = await SeedRelationshipAsync(context.Authentication);

        using var response = await context.Client.PatchAsJsonAsync(
            PatientEndpoint(seeded.Subject.Id.Value),
            new
            {
                profileId = Guid.NewGuid(),
                accountId = Guid.NewGuid(),
                beeexyId = "BXY-NOT-AN-AUTHORITY",
                relationshipType = "Parent",
                status = "Revoked",
                version = 1,
                name = "not-approved"
            });
        var problem = await ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("patient.unsupported_field", problem.ErrorCode);
        await using var dbContext = CreateDbContext();
        var persisted = await dbContext.PatientProfiles.AsNoTracking().SingleAsync(
            profile => profile.Id == seeded.Subject.Id);
        var relationship = await dbContext.CareRelationships.AsNoTracking().SingleAsync(
            value => value.Id == seeded.Relationship.Id);
        Assert.Equal(seeded.Subject.BeeexyId, persisted.BeeexyId);
        Assert.Null(persisted.AccountId);
        Assert.Null(persisted.UpdatedAt);
        Assert.Equal(CareRelationshipStatus.Active, relationship.Status);
        Assert.Equal(CareRelationshipType.Caregiver, relationship.RelationshipType);
    }

    [Fact]
    public async Task MultipleManagers_AreIndependentlyAuthorizedAndRevocationRemovesOnlyOne()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var clientA = factory.CreateApiClient();
        using var clientB = factory.CreateApiClient();
        var managerA = await AuthenticateAsync(factory, clientA);
        var managerB = await AuthenticateAsync(factory, clientB);
        SetBearer(clientA, managerA.AccessToken);
        SetBearer(clientB, managerB.AccessToken);
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = CreateManagedProfile(createdAt);
        var relationshipA = CreateRelationship(managerA, subject.Id, createdAt);
        var relationshipB = CreateRelationship(managerB, subject.Id, createdAt.AddMinutes(1));
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(subject, relationshipA, relationshipB);
            await dbContext.SaveChangesAsync();
        }

        var attempts = await Task.WhenAll(
            clientA.PatchAsJsonAsync(PatientEndpoint(subject.Id.Value), new { }),
            clientB.PatchAsJsonAsync(PatientEndpoint(subject.Id.Value), new { }));
        try
        {
            Assert.All(attempts, response =>
                Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode));
        }
        finally
        {
            foreach (var attempt in attempts)
            {
                attempt.Dispose();
            }
        }

        await using (var dbContext = CreateDbContext())
        {
            var persistedA = await dbContext.CareRelationships.SingleAsync(
                relationship => relationship.Id == relationshipA.Id);
            persistedA.Revoke(EntityId.From(managerA.Account.AccountId), DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        using var aAfter = await clientA.PatchAsJsonAsync(
            PatientEndpoint(subject.Id.Value),
            new { });
        using var bAfter = await clientB.PatchAsJsonAsync(
            PatientEndpoint(subject.Id.Value),
            new { });
        Assert.Equal(HttpStatusCode.NotFound, aAfter.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bAfter.StatusCode);
        await using var verification = CreateDbContext();
        Assert.True(await verification.PatientProfiles.AnyAsync(profile => profile.Id == subject.Id));
        Assert.Equal(
            2,
            await verification.CareRelationships.CountAsync(
                relationship => relationship.SubjectProfileId == subject.Id));
    }

    [Fact]
    public async Task CrossAccountPrimaryManagedAndBeeexyIdentifiers_DoNotGrantAuthority()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var clientA = factory.CreateApiClient();
        using var clientB = factory.CreateApiClient();
        var accountA = await AuthenticateAsync(factory, clientA);
        var accountB = await AuthenticateAsync(factory, clientB);
        SetBearer(clientA, accountA.AccessToken);
        var managedByB = await SeedRelationshipAsync(accountB);

        using var primary = await clientA.PatchAsJsonAsync(
            PatientEndpoint(accountB.Account.ProfileId),
            new { name = "not-approved" });
        using var managed = await clientA.PatchAsJsonAsync(
            PatientEndpoint(managedByB.Subject.Id.Value),
            new { name = "not-approved" });
        using var beeexy = await clientA.PatchAsJsonAsync(
            $"{PatientsEndpoint}/{accountB.Account.BeeexyId}",
            new { name = "not-approved" });

        Assert.Equal(HttpStatusCode.NotFound, primary.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, managed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, beeexy.StatusCode);
    }

    [Fact]
    public async Task MissingAndInvalidBearer_ReturnUnauthorized()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var endpoint = PatientEndpoint(Guid.NewGuid());

        using var missing = await client.PatchAsJsonAsync(endpoint, new { });
        SetBearer(client, "not-a-valid-jwt");
        using var invalid = await client.PatchAsJsonAsync(endpoint, new { });

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [Fact]
    public async Task MalformedUuid_UsesExistingConcealedRouteNotFoundConvention()
    {
        using var context = await CreateAuthenticatedContextAsync();

        using var response = await context.Client.PatchAsJsonAsync(
            $"{PatientsEndpoint}/not-a-uuid",
            new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Phase32CreateReadUpdateAttempt_PreservesCreatedManagedPatient()
    {
        using var context = await CreateAuthenticatedContextAsync();
        using var creationResponse = await context.Client.PostAsJsonAsync(
            RelationshipsEndpoint,
            new
            {
                relationshipType = "Child",
                attestationVersion = "phase-3.6-create-update",
                attestationAccepted = true
            });
        var created = await creationResponse.Content.ReadFromJsonAsync<CreateResponse>();
        Assert.Equal(HttpStatusCode.Created, creationResponse.StatusCode);
        Assert.NotNull(created);

        using var beforeResponse = await context.Client.GetAsync(
            PatientEndpoint(created.Patient.ProfileId));
        var before = await beforeResponse.Content.ReadFromJsonAsync<PatientResponse>();
        using var patchResponse = await context.Client.PatchAsJsonAsync(
            PatientEndpoint(created.Patient.ProfileId),
            new { name = "not-approved" });
        using var afterResponse = await context.Client.GetAsync(
            PatientEndpoint(created.Patient.ProfileId));
        var after = await afterResponse.Content.ReadFromJsonAsync<PatientResponse>();

        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, patchResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        Assert.Equal(before, after);
        Assert.Equal(created.Patient, after);
    }

    [Fact]
    public async Task PatientsMePatch_RetainsExistingPreferenceConcurrencyContract()
    {
        using var context = await CreateAuthenticatedContextAsync();
        using var getResponse = await context.Client.GetAsync("/api/v1/patients/me");
        var original = await getResponse.Content.ReadFromJsonAsync<PrimaryPatientResponse>();
        Assert.NotNull(original);

        using var accepted = await context.Client.PatchAsJsonAsync(
            "/api/v1/patients/me",
            new { timezone = "America/Lima", version = original.Version });
        var updated = await accepted.Content.ReadFromJsonAsync<PrimaryPatientResponse>();
        using var stale = await context.Client.PatchAsJsonAsync(
            "/api/v1/patients/me",
            new { timezone = "America/New_York", version = original.Version });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.NotNull(updated);
        Assert.Equal(original.Version + 1, updated.Version);
        Assert.Equal("America/Lima", updated.Preferences.GetProperty("timezone").GetString());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task OpenApi_AddsOnlyConservativePatientPatchOnExistingDetailPath()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var detail = paths.GetProperty("/api/v1/patients/{patientId}");
        var patch = detail.GetProperty("patch");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12, paths.EnumerateObject().Count());
        Assert.True(detail.TryGetProperty("get", out _));
        Assert.True(patch.TryGetProperty("requestBody", out _));
        Assert.Contains(
            "No PatientProfile fields are currently approved as mutable",
            patch.GetProperty("description").GetString(),
            StringComparison.Ordinal);
        foreach (var status in new[] { "400", "401", "404", "422", "500" })
        {
            Assert.True(patch.GetProperty("responses").TryGetProperty(status, out _));
        }
        Assert.False(patch.GetProperty("responses").TryGetProperty("200", out _));
        Assert.False(patch.GetProperty("responses").TryGetProperty("409", out _));
        var security = Assert.Single(patch.GetProperty("security").EnumerateArray());
        Assert.True(security.TryGetProperty("Bearer", out _));
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

        using var response = await context.Client.PatchAsJsonAsync(
            PatientEndpoint(Guid.NewGuid()),
            new { });
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
        var email = $"patient-update-{Guid.NewGuid():N}@example.com";
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
            AuthorizationAttestation.Create("phase-3.6-test", createdAt),
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

    private sealed record ProblemResponse(
        int Status,
        string Title,
        string? Type,
        string? Detail,
        string? ErrorCode);

    private sealed record PatientResponse(Guid ProfileId, string BeeexyId);

    private sealed record PrimaryPatientResponse(
        Guid ProfileId,
        string BeeexyId,
        JsonElement Preferences,
        long Version);

    private sealed record CreateResponse(
        JsonElement Relationship,
        PatientResponse Patient);

    private sealed record SeededRelationship(
        PatientProfile Subject,
        CareRelationship Relationship);
}
