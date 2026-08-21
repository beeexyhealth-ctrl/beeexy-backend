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
public sealed class MyCircleListingEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string PatientsEndpoint = "/api/v1/patients";
    private const string RelationshipsEndpoint = "/api/v1/care-relationships";

    [Fact]
    public async Task EmptyState_ReturnsPrimaryPatientAndEmptyRelationshipHistory()
    {
        using var context = await CreateAuthenticatedContextAsync();

        var patients = await GetPatientsAsync(context.Client);
        var relationships = await GetRelationshipsAsync(context.Client);

        var primary = Assert.Single(patients.Patients);
        Assert.Equal(context.Authentication.Account.ProfileId, primary.ProfileId);
        Assert.Equal(context.Authentication.Account.BeeexyId, primary.BeeexyId);
        Assert.Equal("Primary", primary.AccessType);
        Assert.Null(primary.Relationship);
        Assert.Empty(relationships.Relationships);
    }

    [Fact]
    public async Task Phase32Creation_AppearsInAccessiblePatientsAndActiveHistory()
    {
        using var context = await CreateAuthenticatedContextAsync();
        using var creationResponse = await context.Client.PostAsJsonAsync(
            RelationshipsEndpoint,
            new
            {
                relationshipType = "Child",
                attestationVersion = "phase-3.3-e2e",
                attestationAccepted = true
            });
        Assert.Equal(HttpStatusCode.Created, creationResponse.StatusCode);
        var created = await creationResponse.Content.ReadFromJsonAsync<CreateResponse>();
        Assert.NotNull(created);

        var patients = await GetPatientsAsync(context.Client);
        var relationships = await GetRelationshipsAsync(context.Client);

        Assert.Equal(2, patients.Patients.Count);
        Assert.Equal("Primary", patients.Patients[0].AccessType);
        var managed = patients.Patients[1];
        Assert.Equal(created.Patient.ProfileId, managed.ProfileId);
        Assert.Equal(created.Patient.BeeexyId, managed.BeeexyId);
        Assert.Equal("Managed", managed.AccessType);
        Assert.Equal(created.Relationship.Id, managed.Relationship?.RelationshipId);
        Assert.Equal("Child", managed.Relationship?.Type);

        var relationship = Assert.Single(relationships.Relationships);
        Assert.Equal(created.Relationship.Id, relationship.Id);
        Assert.Equal(created.Patient.ProfileId, relationship.Subject.ProfileId);
        Assert.Equal("Active", relationship.Status);
        Assert.Equal("phase-3.3-e2e", relationship.AttestationVersion);
    }

    [Fact]
    public async Task ActiveAndRevokedState_SeparatesAccessFromRelationshipHistory()
    {
        using var context = await CreateAuthenticatedContextAsync();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var active = await SeedRelationshipAsync(
            context.Authentication,
            CareRelationshipType.Parent,
            createdAt);
        var revoked = await SeedRelationshipAsync(
            context.Authentication,
            CareRelationshipType.Caregiver,
            createdAt.AddMinutes(2),
            revoked: true);

        var patients = await GetPatientsAsync(context.Client);
        var relationships = await GetRelationshipsAsync(context.Client);

        Assert.Equal(
            new[] { context.Authentication.Account.ProfileId, active.Subject.Id.Value },
            patients.Patients.Select(value => value.ProfileId));
        Assert.DoesNotContain(
            patients.Patients,
            value => value.ProfileId == revoked.Subject.Id.Value);
        Assert.Equal(2, relationships.Relationships.Count);
        Assert.Equal(active.Relationship.Id.Value, relationships.Relationships[0].Id);
        Assert.Equal("Active", relationships.Relationships[0].Status);
        Assert.Equal(revoked.Relationship.Id.Value, relationships.Relationships[1].Id);
        Assert.Equal("Revoked", relationships.Relationships[1].Status);
        Assert.NotNull(relationships.Relationships[1].RevokedAt);
    }

    [Fact]
    public async Task OneManagerMultipleSubjects_AreIsolatedAndDeterministicallyOrdered()
    {
        using var context = await CreateAuthenticatedContextAsync();
        var baseTime = DateTimeOffset.UtcNow.AddHours(-1);
        var last = await SeedRelationshipAsync(
            context.Authentication,
            CareRelationshipType.Sibling,
            baseTime.AddMinutes(3));
        var first = await SeedRelationshipAsync(
            context.Authentication,
            CareRelationshipType.Parent,
            baseTime.AddMinutes(1));
        var second = await SeedRelationshipAsync(
            context.Authentication,
            CareRelationshipType.Child,
            baseTime.AddMinutes(2));
        var unrelated = PatientProfile.Create(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            baseTime);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.PatientProfiles.Add(unrelated);
            await dbContext.SaveChangesAsync();
        }

        var patients = await GetPatientsAsync(context.Client);
        var relationships = await GetRelationshipsAsync(context.Client);

        Assert.Equal(
            new[]
            {
                context.Authentication.Account.ProfileId,
                first.Subject.Id.Value,
                second.Subject.Id.Value,
                last.Subject.Id.Value
            },
            patients.Patients.Select(value => value.ProfileId));
        Assert.Equal(
            new[]
            {
                first.Relationship.Id.Value,
                second.Relationship.Id.Value,
                last.Relationship.Id.Value
            },
            relationships.Relationships.Select(value => value.Id));
        Assert.DoesNotContain(unrelated.Id.Value, patients.Patients.Select(value => value.ProfileId));
        Assert.DoesNotContain(
            unrelated.BeeexyId.Value,
            patients.Patients.Select(value => value.BeeexyId));
    }

    [Fact]
    public async Task MultipleManagers_SeeSharedSubjectThroughSeparateRelationshipsOnly()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var firstClient = factory.CreateApiClient();
        using var secondClient = factory.CreateApiClient();
        var firstAuthentication = await AuthenticateAsync(factory, firstClient);
        var secondAuthentication = await AuthenticateAsync(factory, secondClient);
        SetBearer(firstClient, firstAuthentication.AccessToken);
        SetBearer(secondClient, secondAuthentication.AccessToken);
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var subject = PatientProfile.Create(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            createdAt);
        var firstRelationship = CreateRelationship(
            firstAuthentication,
            subject.Id,
            CareRelationshipType.Parent,
            createdAt);
        var secondRelationship = CreateRelationship(
            secondAuthentication,
            subject.Id,
            CareRelationshipType.Caregiver,
            createdAt.AddMinutes(1));
        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(subject, firstRelationship, secondRelationship);
            await dbContext.SaveChangesAsync();
        }

        var firstPatients = await GetPatientsAsync(firstClient);
        var secondPatients = await GetPatientsAsync(secondClient);
        var firstHistory = await GetRelationshipsAsync(firstClient);
        var secondHistory = await GetRelationshipsAsync(secondClient);

        Assert.Contains(firstPatients.Patients, value => value.ProfileId == subject.Id.Value);
        Assert.Contains(secondPatients.Patients, value => value.ProfileId == subject.Id.Value);
        Assert.DoesNotContain(
            firstPatients.Patients,
            value => value.ProfileId == secondAuthentication.Account.ProfileId);
        Assert.DoesNotContain(
            secondPatients.Patients,
            value => value.ProfileId == firstAuthentication.Account.ProfileId);
        Assert.Equal(
            firstRelationship.Id.Value,
            Assert.Single(firstHistory.Relationships).Id);
        Assert.Equal(
            secondRelationship.Id.Value,
            Assert.Single(secondHistory.Relationships).Id);
    }

    [Fact]
    public async Task SubjectOnlyContext_DoesNotReturnAnotherManagersRelationship()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var subjectClient = factory.CreateApiClient();
        using var managerClient = factory.CreateApiClient();
        var subjectAuthentication = await AuthenticateAsync(factory, subjectClient);
        var managerAuthentication = await AuthenticateAsync(factory, managerClient);
        SetBearer(subjectClient, subjectAuthentication.AccessToken);
        SetBearer(managerClient, managerAuthentication.AccessToken);
        var relationship = CreateRelationship(
            managerAuthentication,
            EntityId.From(subjectAuthentication.Account.ProfileId),
            CareRelationshipType.Caregiver,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        await using (var dbContext = CreateDbContext())
        {
            dbContext.CareRelationships.Add(relationship);
            await dbContext.SaveChangesAsync();
        }

        var subjectPatients = await GetPatientsAsync(subjectClient);
        var subjectHistory = await GetRelationshipsAsync(subjectClient);
        var managerPatients = await GetPatientsAsync(managerClient);
        var managerHistory = await GetRelationshipsAsync(managerClient);

        Assert.Single(subjectPatients.Patients);
        Assert.Empty(subjectHistory.Relationships);
        Assert.Contains(
            managerPatients.Patients,
            value => value.ProfileId == subjectAuthentication.Account.ProfileId);
        Assert.Equal(relationship.Id.Value, Assert.Single(managerHistory.Relationships).Id);
    }

    [Fact]
    public async Task KnownUnrelatedUuidAndBeeexyId_DoNotChangeListingScope()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var firstClient = factory.CreateApiClient();
        using var secondClient = factory.CreateApiClient();
        var firstAuthentication = await AuthenticateAsync(factory, firstClient);
        var secondAuthentication = await AuthenticateAsync(factory, secondClient);
        SetBearer(firstClient, firstAuthentication.AccessToken);
        SetBearer(secondClient, secondAuthentication.AccessToken);
        using var createResponse = await secondClient.PostAsJsonAsync(
            RelationshipsEndpoint,
            new
            {
                relationshipType = "Child",
                attestationVersion = "phase-3.3-isolation",
                attestationAccepted = true
            });
        createResponse.EnsureSuccessStatusCode();
        var secondManaged = await createResponse.Content.ReadFromJsonAsync<CreateResponse>();
        Assert.NotNull(secondManaged);

        using var patientsResponse = await firstClient.GetAsync(PatientsEndpoint);
        using var historyResponse = await firstClient.GetAsync(RelationshipsEndpoint);
        var patientsBody = await patientsResponse.Content.ReadAsStringAsync();
        var historyBody = await historyResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, patientsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.DoesNotContain(secondAuthentication.Account.ProfileId.ToString(), patientsBody);
        Assert.DoesNotContain(secondAuthentication.Account.BeeexyId, patientsBody);
        Assert.DoesNotContain(secondManaged.Patient.ProfileId.ToString(), patientsBody);
        Assert.DoesNotContain(secondManaged.Patient.BeeexyId, patientsBody);
        Assert.DoesNotContain(secondManaged.Relationship.Id.ToString(), historyBody);
    }

    [Theory]
    [InlineData(PatientsEndpoint)]
    [InlineData(RelationshipsEndpoint)]
    public async Task MissingBearer_ReturnsUnauthorized(string endpoint)
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(PatientsEndpoint)]
    [InlineData(RelationshipsEndpoint)]
    public async Task InvalidBearer_ReturnsUnauthorizedWithoutTokenDetail(string endpoint)
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        SetBearer(client, "not-a-valid-jwt");

        using var response = await client.GetAsync(endpoint);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PatientsEndpoint)]
    [InlineData(RelationshipsEndpoint)]
    public async Task DisabledAccount_ReturnsGenericUnauthorized(string endpoint)
    {
        using var context = await CreateAuthenticatedContextAsync();
        await using (var dbContext = CreateDbContext())
        {
            var accountId = EntityId.From(context.Authentication.Account.AccountId);
            var account = await dbContext.Accounts.SingleAsync(value => value.Id == accountId);
            account.Disable(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        using var response = await context.Client.GetAsync(endpoint);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("disabled", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PatientsEndpoint)]
    [InlineData(RelationshipsEndpoint)]
    public async Task MissingPrimaryProfile_ReturnsSafeInvariantFailure(string endpoint)
    {
        using var context = await CreateAuthenticatedContextAsync();
        await using (var dbContext = CreateDbContext())
        {
            var profileId = EntityId.From(context.Authentication.Account.ProfileId);
            var profile = await dbContext.PatientProfiles.SingleAsync(value => value.Id == profileId);
            dbContext.PatientProfiles.Remove(profile);
            await dbContext.SaveChangesAsync();
        }

        using var response = await context.Client.GetAsync(endpoint);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("primary-profile-count", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenApi_DocumentsListingsAndPatientDetailWithoutFutureMutations()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var patientsGet = paths.GetProperty(PatientsEndpoint).GetProperty("get");
        var relationshipsPath = paths.GetProperty(RelationshipsEndpoint);
        var relationshipsGet = relationshipsPath.GetProperty("get");

        Assert.True(relationshipsPath.TryGetProperty("post", out _));
        AssertOperation(patientsGet);
        AssertOperation(relationshipsGet);
        var patientDetail = paths.GetProperty("/api/v1/patients/{patientId}");
        Assert.True(patientDetail.TryGetProperty("get", out _));
        Assert.False(patientDetail.TryGetProperty("patch", out _));
        Assert.False(paths.TryGetProperty("/api/v1/care-relationships/{id}", out _));
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
        var email = $"circle-list-{Guid.NewGuid():N}@example.com";
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
        var result = await verificationResponse.Content.ReadFromJsonAsync<AuthenticationResult>();
        return Assert.IsType<AuthenticationResult>(result);
    }

    private async Task<SeededRelationship> SeedRelationshipAsync(
        AuthenticationResult authentication,
        CareRelationshipType relationshipType,
        DateTimeOffset createdAt,
        bool revoked = false)
    {
        var subject = PatientProfile.Create(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            createdAt);
        var relationship = CreateRelationship(
            authentication,
            subject.Id,
            relationshipType,
            createdAt);
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

    private static CareRelationship CreateRelationship(
        AuthenticationResult authentication,
        EntityId subjectProfileId,
        CareRelationshipType relationshipType,
        DateTimeOffset createdAt)
    {
        return CareRelationship.Create(
            EntityId.From(authentication.Account.ProfileId),
            subjectProfileId,
            relationshipType,
            EntityId.From(authentication.Account.AccountId),
            AuthorizationAttestation.Create("phase-3.3-test", createdAt),
            createdAt);
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

    private static void AssertOperation(JsonElement operation)
    {
        var security = operation.GetProperty("security");
        Assert.Single(security.EnumerateArray());
        Assert.True(security[0].TryGetProperty("Bearer", out _));
        foreach (var status in new[] { "200", "401", "500" })
        {
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        }
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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

    private sealed record AccessiblePatientsResponse(
        IReadOnlyList<AccessiblePatientResponse> Patients);

    private sealed record AccessiblePatientResponse(
        Guid ProfileId,
        string BeeexyId,
        string AccessType,
        AccessiblePatientRelationshipResponse? Relationship);

    private sealed record AccessiblePatientRelationshipResponse(
        Guid RelationshipId,
        string Type);

    private sealed record CareRelationshipsResponse(
        IReadOnlyList<CareRelationshipResponse> Relationships);

    private sealed record CareRelationshipResponse(
        Guid Id,
        CareRelationshipSubjectResponse Subject,
        string Type,
        string Status,
        string AttestationVersion,
        DateTimeOffset AttestedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset? RevokedAt);

    private sealed record CareRelationshipSubjectResponse(Guid ProfileId, string BeeexyId);

    private sealed record CreateResponse(
        CreatedRelationshipResponse Relationship,
        CreatedPatientResponse Patient);

    private sealed record CreatedRelationshipResponse(Guid Id);

    private sealed record CreatedPatientResponse(Guid ProfileId, string BeeexyId);

    private sealed record SeededRelationship(
        PatientProfile Subject,
        CareRelationship Relationship);
}
