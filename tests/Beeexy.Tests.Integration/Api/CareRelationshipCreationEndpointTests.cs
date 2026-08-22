using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Patients;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class CareRelationshipCreationEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string Endpoint = "/api/v1/care-relationships";

    [Fact]
    public async Task ValidRequest_CreatesExactlyOneUnownedPatientAndActiveRelationship()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authentication.AccessToken);
        var before = DateTimeOffset.UtcNow;

        using var response = await client.PostAsJsonAsync(Endpoint, ValidRequest("Child"));

        var after = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateResponse>();
        Assert.NotNull(body);
        Assert.Equal("Child", body.Relationship.Type);
        Assert.Equal("Active", body.Relationship.Status);
        Assert.Equal("phase-3.2-test", body.Relationship.AttestationVersion);
        Assert.Equal("Maria", body.Patient.FirstName);
        Assert.Equal("Arias", body.Patient.LastName);
        Assert.Equal(new DateOnly(2012, 5, 12), body.Patient.DateOfBirth);
        Assert.Equal("Female", body.Patient.SexAssignedAtBirth);
        Assert.Equal("NY", body.Patient.State);
        Assert.Equal(1, body.Patient.Version);
        Assert.InRange(body.Relationship.AttestedAt, before, after);
        Assert.Equal($"/api/v1/patients/{body.Patient.ProfileId}", response.Headers.Location?.ToString());

        await using var dbContext = CreateDbContext();
        var accountId = EntityId.From(authentication.AccountId);
        var subjectId = EntityId.From(body.Patient.ProfileId);
        var relationshipId = EntityId.From(body.Relationship.Id);
        var account = await dbContext.Accounts.SingleAsync(value =>
            value.Id == accountId);
        var profiles = await dbContext.PatientProfiles
            .Where(value => value.AccountId == account.Id || value.Id == subjectId)
            .ToListAsync();
        var relationship = await dbContext.CareRelationships
            .SingleAsync(value => value.Id == relationshipId);
        var manager = Assert.Single(profiles, value => value.AccountId == account.Id);
        var subject = Assert.Single(profiles, value => value.Id.Value == body.Patient.ProfileId);

        Assert.Equal(2, profiles.Count);
        Assert.Null(subject.AccountId);
        Assert.Equal(body.Patient.BeeexyId, subject.BeeexyId.Value);
        Assert.Equal(body.Patient.FirstName, subject.FirstName?.Value);
        Assert.Equal(body.Patient.LastName, subject.LastName?.Value);
        Assert.Equal(body.Patient.DateOfBirth, subject.DateOfBirth);
        Assert.Equal(SexAssignedAtBirth.Female, subject.SexAssignedAtBirth);
        Assert.Equal(body.Patient.State, subject.State?.Code);
        Assert.Equal(body.Patient.Version, subject.Version);
        Assert.Equal(manager.Id, relationship.ManagerProfileId);
        Assert.Equal(subject.Id, relationship.SubjectProfileId);
        Assert.Equal(account.Id, relationship.CreatedByAccountId);
        Assert.Equal(CareRelationshipStatus.Active, relationship.Status);
        Assert.Equal("phase-3.2-test", relationship.Attestation.Version);
        Assert.Equal(
            body.Relationship.AttestedAt.ToUnixTimeMilliseconds(),
            relationship.Attestation.AttestedAt.ToUnixTimeMilliseconds());
        Assert.Equal(1, await dbContext.Accounts.CountAsync(value => value.Id == account.Id));
        Assert.Equal(1, await dbContext.RefreshSessions.CountAsync(value => value.AccountId == account.Id));
        Assert.Empty(await dbContext.ExternalIdentities
            .Where(value => value.AccountId == account.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task NoBearerToken_ReturnsUnauthorizedAndCreatesNothing()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var profileCount = await CountProfilesAsync();
        var relationshipCount = await CountRelationshipsAsync();

        using var response = await client.PostAsJsonAsync(Endpoint, ValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(profileCount, await CountProfilesAsync());
        Assert.Equal(relationshipCount, await CountRelationshipsAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Friend")]
    [InlineData("0")]
    public async Task InvalidRelationshipType_ReturnsSafeUnprocessableEntity(string? type)
    {
        using var context = await CreateAuthenticatedContextAsync();

        using var response = await context.Client.PostAsJsonAsync(
            Endpoint,
            ValidRequest(type));

        await AssertValidationProblemAsync(response, "care_relationship.invalid_type");
    }

    [Fact]
    public async Task MissingAttestationAcceptance_ReturnsSafeUnprocessableEntity()
    {
        using var context = await CreateAuthenticatedContextAsync();

        using var response = await context.Client.PostAsJsonAsync(
            Endpoint,
            new
            {
                relationshipType = "Parent",
                attestationVersion = "phase-3.2-test",
                patient = ValidPatient()
            });

        await AssertValidationProblemAsync(
            response,
            "care_relationship.attestation_required");
    }

    [Fact]
    public async Task MissingAttestationVersion_ReturnsSafeUnprocessableEntity()
    {
        using var context = await CreateAuthenticatedContextAsync();

        using var response = await context.Client.PostAsJsonAsync(
            Endpoint,
            new
            {
                relationshipType = "Parent",
                attestationAccepted = true,
                patient = ValidPatient()
            });

        await AssertValidationProblemAsync(
            response,
            "care_relationship.invalid_attestation_version");
    }

    [Fact]
    public async Task MissingPatientDemographics_ReturnsSafeUnprocessableEntity()
    {
        using var context = await CreateAuthenticatedContextAsync();

        using var response = await context.Client.PostAsJsonAsync(
            Endpoint,
            new
            {
                relationshipType = "Parent",
                attestationVersion = "phase-3.2-test",
                attestationAccepted = true
            });

        await AssertValidationProblemAsync(response, "patient.demographics_required");
    }

    [Theory]
    [InlineData("firstName", "", "patient.invalid_first_name")]
    [InlineData("lastName", "   ", "patient.invalid_last_name")]
    [InlineData("dateOfBirth", "2999-01-01", "patient.invalid_date_of_birth")]
    [InlineData("dateOfBirth", "05/12/2012", "patient.invalid_date_of_birth")]
    [InlineData("sexAssignedAtBirth", "female", "patient.invalid_sex_assigned_at_birth")]
    [InlineData("sexAssignedAtBirth", "Unknown", "patient.invalid_sex_assigned_at_birth")]
    [InlineData("state", "XX", "patient.invalid_state")]
    public async Task InvalidPatientDemographic_ReturnsSafeUnprocessableEntity(
        string field,
        string value,
        string expectedCode)
    {
        using var context = await CreateAuthenticatedContextAsync();
        var patient = new Dictionary<string, object?>
        {
            ["firstName"] = "Maria",
            ["lastName"] = "Arias",
            ["dateOfBirth"] = "2012-05-12",
            ["sexAssignedAtBirth"] = "Female",
            ["state"] = "NY"
        };
        patient[field] = value;

        using var response = await context.Client.PostAsJsonAsync(
            Endpoint,
            new
            {
                relationshipType = "Parent",
                attestationVersion = "phase-3.2-test",
                attestationAccepted = true,
                patient
            });

        await AssertValidationProblemAsync(response, expectedCode);
    }

    [Fact]
    public async Task MalformedRequest_ReturnsSafeBadRequest()
    {
        using var context = await CreateAuthenticatedContextAsync();

        using var response = await context.Client.PostAsync(
            Endpoint,
            new StringContent(
                "{\"relationshipType\":",
                Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JsonException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestCannotSupplyManagerOrSubjectIdentity()
    {
        using var context = await CreateAuthenticatedContextAsync();

        using var response = await context.Client.PostAsJsonAsync(
            Endpoint,
            new
            {
                relationshipType = "Parent",
                attestationVersion = "phase-3.2-test",
                attestationAccepted = true,
                managerProfileId = Guid.NewGuid(),
                subjectBeeexyId = "BXY-SPOOFED"
            });

        await AssertValidationProblemAsync(
            response,
            "care_relationship.unsupported_field");
    }

    [Fact]
    public async Task DisabledAccount_ReturnsGenericUnauthorizedAndCreatesNothing()
    {
        using var context = await CreateAuthenticatedContextAsync();
        await using (var dbContext = CreateDbContext())
        {
            var accountId = EntityId.From(context.Authentication.AccountId);
            var account = await dbContext.Accounts.SingleAsync(value =>
                value.Id == accountId);
            account.Disable(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }
        var profileCount = await CountProfilesAsync();

        using var response = await context.Client.PostAsJsonAsync(Endpoint, ValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("disabled", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(profileCount, await CountProfilesAsync());
        Assert.Equal(0, await CountRelationshipsForAccountAsync(context.Authentication.AccountId));
    }

    [Fact]
    public async Task MissingPrimaryManagerProfile_ReturnsSafeServerFailureWithoutCreation()
    {
        using var context = await CreateAuthenticatedContextAsync();
        int unownedProfileCount;
        await using (var dbContext = CreateDbContext())
        {
            var accountId = EntityId.From(context.Authentication.AccountId);
            var profile = await dbContext.PatientProfiles.SingleAsync(value =>
                value.AccountId == accountId);
            dbContext.PatientProfiles.Remove(profile);
            await dbContext.SaveChangesAsync();
            unownedProfileCount = await dbContext.PatientProfiles.CountAsync(value =>
                value.AccountId == null);
        }

        using var response = await context.Client.PostAsJsonAsync(Endpoint, ValidRequest());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("primary-profile-count", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await CountRelationshipsForAccountAsync(context.Authentication.AccountId));
        await using var verification = CreateDbContext();
        Assert.Equal(
            unownedProfileCount,
            await verification.PatientProfiles.CountAsync(value => value.AccountId == null));
    }

    [Fact]
    public async Task RelationshipConflict_RollsBackManagedProfileAndReturnsSafeConflict()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<IManagedPatientCreationRepository>();
                services.AddScoped<IManagedPatientCreationRepository>(provider =>
                    new DuplicateRelationshipRepository(
                        new ManagedPatientCreationRepository(
                            provider.GetRequiredService<BeeexyDbContext>()),
                        provider.GetRequiredService<BeeexyDbContext>()));
            });
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authentication.AccessToken);
        var profileCount = await CountProfilesAsync();
        var relationshipCount = await CountRelationshipsAsync();

        using var response = await client.PostAsJsonAsync(Endpoint, ValidRequest());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ux_care_relationships", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Postgres", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(profileCount, await CountProfilesAsync());
        Assert.Equal(relationshipCount, await CountRelationshipsAsync());
    }

    [Fact]
    public async Task OneManager_CanCreateMultipleIndependentManagedPatients()
    {
        using var context = await CreateAuthenticatedContextAsync();
        var createdPatients = new List<CreateResponse>();

        foreach (var type in new[] { "Parent", "Caregiver", "Sibling" })
        {
            using var response = await context.Client.PostAsJsonAsync(Endpoint, ValidRequest(type));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            createdPatients.Add((await response.Content.ReadFromJsonAsync<CreateResponse>())!);
        }

        Assert.Equal(3, createdPatients.Select(value => value.Patient.ProfileId).Distinct().Count());
        Assert.Equal(3, createdPatients.Select(value => value.Patient.BeeexyId).Distinct().Count());
        await using var dbContext = CreateDbContext();
        var accountId = EntityId.From(context.Authentication.AccountId);
        var manager = await dbContext.PatientProfiles.SingleAsync(value =>
            value.AccountId == accountId);
        Assert.Equal(3, await dbContext.CareRelationships.CountAsync(value =>
            value.ManagerProfileId == manager.Id &&
            value.Status == CareRelationshipStatus.Active));
        var createdPatientIds = createdPatients
            .Select(created => EntityId.From(created.Patient.ProfileId))
            .ToArray();
        Assert.Equal(3, await dbContext.PatientProfiles.CountAsync(value =>
            value.AccountId == null &&
            createdPatientIds.Contains(value.Id)));
    }

    [Fact]
    public async Task OpenApi_DocumentsOnlyImplementedCareRelationshipPost()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var operation = paths.GetProperty(Endpoint).GetProperty("post");

        Assert.True(operation.TryGetProperty("security", out _));
        foreach (var status in new[] { "201", "400", "401", "409", "422", "500" })
        {
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        }

        Assert.True(paths
            .GetProperty("/api/v1/care-relationships/{id}")
            .TryGetProperty("delete", out _));
        var patientDetail = paths.GetProperty("/api/v1/patients/{patientId}");
        Assert.True(patientDetail.TryGetProperty("get", out _));
        Assert.True(patientDetail.TryGetProperty("patch", out _));
    }

    private async Task<AuthenticatedContext> CreateAuthenticatedContextAsync()
    {
        await EnsureMigratedAsync();
        var factory = new BeeexyApiFactory(postgres.ConnectionString);
        var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authentication.AccessToken);
        return new AuthenticatedContext(factory, client, authentication);
    }

    private async Task<AuthenticationResult> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client)
    {
        var email = $"care-create-{Guid.NewGuid():N}@example.com";
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

    private async Task<int> CountProfilesAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.PatientProfiles.CountAsync();
    }

    private async Task<int> CountRelationshipsAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.CareRelationships.CountAsync();
    }

    private async Task<int> CountRelationshipsForAccountAsync(Guid accountId)
    {
        await using var dbContext = CreateDbContext();
        var entityAccountId = EntityId.From(accountId);
        var managerIds = dbContext.PatientProfiles
            .Where(value => value.AccountId == entityAccountId)
            .Select(value => value.Id);
        return await dbContext.CareRelationships.CountAsync(value =>
            managerIds.Contains(value.ManagerProfileId));
    }

    private static object ValidRequest(string? relationshipType = "Parent") => new
    {
        relationshipType,
        attestationVersion = "phase-3.2-test",
        attestationAccepted = true,
        patient = ValidPatient()
    };

    private static object ValidPatient() => new
    {
        firstName = "Maria",
        lastName = "Arias",
        dateOfBirth = "2012-05-12",
        sexAssignedAtBirth = "Female",
        state = "NY"
    };

    private static async Task AssertValidationProblemAsync(
        HttpResponseMessage response,
        string errorCode)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(errorCode, document.RootElement.GetProperty("errorCode").GetString());
        var body = document.RootElement.ToString();
        Assert.DoesNotContain("Postgres", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DuplicateRelationshipRepository(
        IManagedPatientCreationRepository inner,
        BeeexyDbContext dbContext) : IManagedPatientCreationRepository
    {
        public void Add(PatientProfile subject, CareRelationship relationship)
        {
            inner.Add(subject, relationship);
            var duplicate = CareRelationship.Create(
                relationship.ManagerProfileId,
                relationship.SubjectProfileId,
                relationship.RelationshipType,
                relationship.CreatedByAccountId,
                AuthorizationAttestation.Create(
                    relationship.Attestation.Version,
                    relationship.Attestation.AttestedAt),
                relationship.CreatedAt);
            dbContext.CareRelationships.Add(duplicate);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);
    }

    private sealed class AuthenticatedContext(
        BeeexyApiFactory factory,
        HttpClient client,
        AuthenticationResult authentication) : IDisposable
    {
        public BeeexyApiFactory Factory { get; } = factory;

        public HttpClient Client { get; } = client;

        public AuthenticationResult Authentication { get; } = authentication;

        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }

    private sealed record AuthenticationResult(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account)
    {
        public Guid AccountId => Account.AccountId;
    }

    private sealed record AuthenticationAccount(Guid AccountId, Guid ProfileId, string BeeexyId);

    private sealed record CreateResponse(
        RelationshipResponse Relationship,
        PatientResponse Patient);

    private sealed record RelationshipResponse(
        Guid Id,
        string Type,
        string Status,
        string AttestationVersion,
        DateTimeOffset AttestedAt);

    private sealed record PatientResponse(
        Guid ProfileId,
        string BeeexyId,
        string FirstName,
        string LastName,
        DateOnly DateOfBirth,
        string SexAssignedAtBirth,
        string State,
        long Version);
}
