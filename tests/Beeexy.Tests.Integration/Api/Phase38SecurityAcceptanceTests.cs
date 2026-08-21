using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class Phase38SecurityAcceptanceTests(PostgreSqlContainerFixture postgres)
{
    private const string SigningKey =
        "integration-test-only-jwt-signing-key-with-at-least-32-bytes";
    private const string Issuer = "https://api.beeexy.com";
    private const string Audience = "beeexy-client";
    private const string PatientsEndpoint = "/api/v1/patients";
    private const string RelationshipsEndpoint = "/api/v1/care-relationships";

    [Fact]
    public async Task AllSixPhase3Endpoints_RejectTheCompleteInvalidBearerMatrix()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "jwt-matrix");
        var now = DateTimeOffset.UtcNow;
        var credentials = new string?[]
        {
            null,
            "not-a-valid-jwt",
            CreateJwt(authentication.Account.AccountId, Issuer, Audience, now, now.AddMinutes(5),
                "wrong-signing-key-with-at-least-thirty-two-bytes"),
            CreateJwt(authentication.Account.AccountId, "https://wrong-issuer.example", Audience,
                now, now.AddMinutes(5), SigningKey),
            CreateJwt(authentication.Account.AccountId, Issuer, "wrong-audience", now,
                now.AddMinutes(5), SigningKey),
            CreateJwt(authentication.Account.AccountId, Issuer, Audience, now.AddMinutes(-5),
                now.AddMinutes(-1), SigningKey),
            authentication.Account.BeeexyId
        };

        foreach (var credential in credentials)
        {
            for (var endpoint = 0; endpoint < 6; endpoint++)
            {
                using var request = CreatePhase3Request(endpoint, credential);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task DisabledAccount_IsRejectedBeforePhase3RequestValidationOnAllSixEndpoints()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "disabled-matrix");
        await using (var dbContext = CreateDbContext())
        {
            var accountId = EntityId.From(authentication.Account.AccountId);
            var account = await dbContext.Accounts.SingleAsync(value => value.Id == accountId);
            account.Disable(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        for (var endpoint = 0; endpoint < 6; endpoint++)
        {
            using var request = CreatePhase3Request(
                endpoint,
                authentication.AccessToken,
                intentionallyInvalidBody: true);
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain("disabled", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("validation", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task MandatoryAccountAAccountBPatientXJourney_ClosesPhase3Acceptance()
    {
        await EnsureMigratedAsync();
        using var logger = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: logger);
        using var clientA = factory.CreateApiClient();
        using var clientB = factory.CreateApiClient();
        var accountA = await AuthenticateAsync(factory, clientA, "manager-a");
        var accountB = await AuthenticateAsync(factory, clientB, "manager-b");
        SetBearer(clientA, accountA.AccessToken);
        SetBearer(clientB, accountB.AccessToken);
        var identityCountsBefore = await LoadIdentityCountsAsync();
        var beforeCreation = DateTimeOffset.UtcNow;

        using var creationResponse = await clientA.PostAsJsonAsync(
            RelationshipsEndpoint,
            new
            {
                relationshipType = "Child",
                attestationVersion = "phase-3.8-acceptance",
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
        var afterCreation = DateTimeOffset.UtcNow;
        var created = await creationResponse.Content.ReadFromJsonAsync<CreateResponse>();

        Assert.Equal(HttpStatusCode.Created, creationResponse.StatusCode);
        Assert.NotNull(created);
        Assert.Equal("Child", created.Relationship.Type);
        Assert.Equal("Active", created.Relationship.Status);
        Assert.Equal("phase-3.8-acceptance", created.Relationship.AttestationVersion);
        Assert.InRange(created.Relationship.AttestedAt, beforeCreation, afterCreation);
        Assert.Equal("Maria", created.Patient.FirstName);
        Assert.Equal("Arias", created.Patient.LastName);
        Assert.Equal(new DateOnly(2012, 5, 12), created.Patient.DateOfBirth);
        Assert.Equal("Female", created.Patient.SexAssignedAtBirth);
        Assert.Equal("NY", created.Patient.State);
        Assert.Equal(1, created.Patient.Version);

        var patientId = EntityId.From(created.Patient.ProfileId);
        var relationshipAId = EntityId.From(created.Relationship.Id);
        await AssertSinglePatientModelAndNoManagedIdentityAsync(
            accountA,
            accountB,
            patientId,
            identityCountsBefore);

        var patientsA = await GetPatientsAsync(clientA);
        Assert.Equal(accountA.Account.ProfileId, patientsA.Patients[0].ProfileId);
        var managedSummary = Assert.Single(
            patientsA.Patients,
            value => value.ProfileId == patientId.Value);
        Assert.Equal("Managed", managedSummary.AccessType);
        Assert.Equal("Maria", managedSummary.FirstName);
        Assert.Equal("Arias", managedSummary.LastName);
        Assert.NotNull(managedSummary.Relationship);
        Assert.Equal(created.Relationship.Id, managedSummary.Relationship.RelationshipId);

        var relationshipsA = await GetRelationshipsAsync(clientA);
        var activeA = Assert.Single(
            relationshipsA.Relationships,
            value => value.Id == relationshipAId.Value);
        Assert.Equal("Active", activeA.Status);
        Assert.Equal(patientId.Value, activeA.Subject.ProfileId);

        var detailA = await GetPatientAsync(clientA, patientId.Value, HttpStatusCode.OK);
        Assert.Equal("NY", detailA!.State);
        using var patchA = await clientA.PatchAsJsonAsync(
            PatientEndpoint(patientId.Value),
            new { state = "FL", version = detailA.Version });
        var patchedA = await patchA.Content.ReadFromJsonAsync<PatientResponse>();
        Assert.Equal(HttpStatusCode.OK, patchA.StatusCode);
        Assert.NotNull(patchedA);
        Assert.Equal("FL", patchedA.State);
        Assert.Equal(2, patchedA.Version);

        using var deniedBRead = await clientB.GetAsync(PatientEndpoint(patientId.Value));
        using var deniedBPatch = await clientB.PatchAsJsonAsync(
            PatientEndpoint(patientId.Value),
            new { unsupported = "must-remain-concealed" });
        using var missingBRead = await clientB.GetAsync(PatientEndpoint(Guid.NewGuid()));
        var deniedProblem = await ReadProblemAsync(deniedBRead);
        var missingProblem = await ReadProblemAsync(missingBRead);
        Assert.Equal(HttpStatusCode.NotFound, deniedBRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedBPatch.StatusCode);
        AssertPubliclyEquivalent(missingProblem, deniedProblem);

        var relationshipB = CareRelationship.Create(
            EntityId.From(accountB.Account.ProfileId),
            patientId,
            CareRelationshipType.Caregiver,
            EntityId.From(accountB.Account.AccountId),
            AuthorizationAttestation.Create("phase-3.8-structural-manager", DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.CareRelationships.Add(relationshipB);
            await dbContext.SaveChangesAsync();
        }

        Assert.NotNull(await GetPatientAsync(clientA, patientId.Value, HttpStatusCode.OK));
        Assert.NotNull(await GetPatientAsync(clientB, patientId.Value, HttpStatusCode.OK));
        var concurrent = await Task.WhenAll(
            clientA.PatchAsJsonAsync(
                PatientEndpoint(patientId.Value),
                new { firstName = "Maria A", version = 2 }),
            clientB.PatchAsJsonAsync(
                PatientEndpoint(patientId.Value),
                new { firstName = "Maria B", version = 2 }));
        try
        {
            Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in concurrent)
            {
                response.Dispose();
            }
        }

        var winner = await GetPatientAsync(clientB, patientId.Value, HttpStatusCode.OK);
        Assert.NotNull(winner);
        Assert.Contains(winner.FirstName, new[] { "Maria A", "Maria B" });
        Assert.Equal(3, winner.Version);

        using var revokeA = await clientA.DeleteAsync(RelationshipEndpoint(relationshipAId.Value));
        Assert.Equal(HttpStatusCode.NoContent, revokeA.StatusCode);
        Assert.DoesNotContain(
            (await GetPatientsAsync(clientA)).Patients,
            value => value.ProfileId == patientId.Value);
        using var revokedReadA = await clientA.GetAsync(PatientEndpoint(patientId.Value));
        using var revokedPatchA = await clientA.PatchAsJsonAsync(
            PatientEndpoint(patientId.Value),
            new { unsupported = "still-concealed" });
        Assert.Equal(HttpStatusCode.NotFound, revokedReadA.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revokedPatchA.StatusCode);
        var historyA = await GetRelationshipsAsync(clientA);
        var revokedA = Assert.Single(
            historyA.Relationships,
            value => value.Id == relationshipAId.Value);
        Assert.Equal("Revoked", revokedA.Status);
        Assert.NotNull(revokedA.RevokedAt);

        var stillAuthorizedB = await GetPatientAsync(clientB, patientId.Value, HttpStatusCode.OK);
        Assert.Equal(winner, stillAuthorizedB);
        await AssertPersistedMultipleManagerOutcomeAsync(
            patientId,
            relationshipAId,
            relationshipB.Id,
            winner);

        var combinedLogs = string.Join('\n', logger.Messages);
        foreach (var demographicValue in new[] { "Maria", "Arias", "2012-05-12", "NY", "FL" })
        {
            Assert.DoesNotContain(demographicValue, combinedLogs, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RevocationWinningTheRowLock_PreventsAnAlreadyAuthorizedManagedPatch()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var manager = await AuthenticateAsync(factory, client, "revoke-update-race");
        SetBearer(client, manager.AccessToken);
        var created = await CreateManagedPatientAsync(client);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText =
                "SELECT id FROM patients.care_relationships WHERE id = @id FOR UPDATE";
            lockCommand.Parameters.AddWithValue("id", created.Relationship.Id);
            Assert.Equal(created.Relationship.Id, await lockCommand.ExecuteScalarAsync());
        }

        var patchTask = client.PatchAsJsonAsync(
            PatientEndpoint(created.Patient.ProfileId),
            new { state = "FL", version = 1 });
        await Task.Delay(250);
        Assert.False(patchTask.IsCompleted);

        var revokedAt = DateTimeOffset.UtcNow;
        await using (var revokeCommand = connection.CreateCommand())
        {
            revokeCommand.Transaction = transaction;
            revokeCommand.CommandText =
                "UPDATE patients.care_relationships SET status = 'revoked', " +
                "revoked_at = @revokedAt, revoked_by_account_id = @accountId, " +
                "updated_at = @revokedAt WHERE id = @id";
            revokeCommand.Parameters.AddWithValue("id", created.Relationship.Id);
            revokeCommand.Parameters.AddWithValue("accountId", manager.Account.AccountId);
            revokeCommand.Parameters.AddWithValue("revokedAt", revokedAt);
            Assert.Equal(1, await revokeCommand.ExecuteNonQueryAsync());
        }
        await transaction.CommitAsync();

        using var patch = await patchTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        await using var dbContext = CreateDbContext();
        var profileId = EntityId.From(created.Patient.ProfileId);
        var profile = await dbContext.PatientProfiles.AsNoTracking()
            .SingleAsync(value => value.Id == profileId);
        Assert.Equal("NY", profile.State?.Code);
        Assert.Equal(1, profile.Version);
    }

    [Fact]
    public async Task OpenApi_ClosesTheExactPhase3SurfaceAndRelationshipEnums()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var operations = new[]
        {
            paths.GetProperty(PatientsEndpoint).GetProperty("get"),
            paths.GetProperty(RelationshipsEndpoint).GetProperty("post"),
            paths.GetProperty(RelationshipsEndpoint).GetProperty("get"),
            paths.GetProperty("/api/v1/patients/{patientId}").GetProperty("get"),
            paths.GetProperty("/api/v1/patients/{patientId}").GetProperty("patch"),
            paths.GetProperty("/api/v1/care-relationships/{id}").GetProperty("delete")
        };
        Assert.All(operations, operation =>
        {
            var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
            Assert.True(security.TryGetProperty("Bearer", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _));
        });
        Assert.DoesNotContain(paths.EnumerateObject(), path =>
            path.Name.Contains("sharing", StringComparison.OrdinalIgnoreCase) ||
            path.Name.Contains("invitation", StringComparison.OrdinalIgnoreCase) ||
            path.Name.Contains("claim", StringComparison.OrdinalIgnoreCase));

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal(
            new[]
            {
                "Parent",
                "LegalGuardian",
                "Caregiver",
                "Spouse",
                "Child",
                "Sibling",
                "Other"
            },
            schemas.GetProperty("CreateManagedPatientRequest")
                .GetProperty("properties")
                .GetProperty("relationshipType")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            new[] { "Active", "Revoked" },
            schemas.GetProperty("CareRelationshipResponse")
                .GetProperty("properties")
                .GetProperty("status")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
    }

    private static HttpRequestMessage CreatePhase3Request(
        int endpoint,
        string? credential,
        bool intentionallyInvalidBody = false)
    {
        var id = Guid.NewGuid();
        var request = endpoint switch
        {
            0 => new HttpRequestMessage(HttpMethod.Get, PatientsEndpoint),
            1 => new HttpRequestMessage(HttpMethod.Post, RelationshipsEndpoint)
            {
                Content = JsonContent.Create(intentionallyInvalidBody
                    ? (object)new { managerProfileId = Guid.NewGuid() }
                    : new
                    {
                        relationshipType = "Child",
                        attestationVersion = "phase-3.8-jwt-matrix",
                        attestationAccepted = true,
                        patient = new
                        {
                            firstName = "Maria",
                            lastName = "Arias",
                            dateOfBirth = "2012-05-12",
                            sexAssignedAtBirth = "Female",
                            state = "NY"
                        }
                    })
            },
            2 => new HttpRequestMessage(HttpMethod.Get, RelationshipsEndpoint),
            3 => new HttpRequestMessage(HttpMethod.Get, PatientEndpoint(id)),
            4 => new HttpRequestMessage(HttpMethod.Patch, PatientEndpoint(id))
            {
                Content = JsonContent.Create(intentionallyInvalidBody
                    ? (object)new { unsupported = "value" }
                    : new { firstName = "Maria", version = 1 })
            },
            5 => new HttpRequestMessage(HttpMethod.Delete, RelationshipEndpoint(id)),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
        };
        if (credential is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }

        return request;
    }

    private async Task AssertSinglePatientModelAndNoManagedIdentityAsync(
        AuthenticationResponse accountA,
        AuthenticationResponse accountB,
        EntityId patientId,
        IdentityCounts identityCountsBefore)
    {
        await using var dbContext = CreateDbContext();
        var primaryIds = new[]
        {
            EntityId.From(accountA.Account.ProfileId),
            EntityId.From(accountB.Account.ProfileId)
        };
        var primaryProfiles = await dbContext.PatientProfiles.AsNoTracking()
            .Where(value => primaryIds.Contains(value.Id))
            .ToArrayAsync();
        Assert.Equal(2, primaryProfiles.Length);
        Assert.All(primaryProfiles, profile => Assert.NotNull(profile.AccountId));
        var managed = await dbContext.PatientProfiles.AsNoTracking()
            .SingleAsync(value => value.Id == patientId);
        Assert.Null(managed.AccountId);
        Assert.Equal(identityCountsBefore.Accounts, await dbContext.Accounts.CountAsync());
        Assert.Equal(
            identityCountsBefore.ExternalIdentities,
            await dbContext.ExternalIdentities.CountAsync());
        Assert.Equal(identityCountsBefore.Sessions, await dbContext.RefreshSessions.CountAsync());
    }

    private async Task AssertPersistedMultipleManagerOutcomeAsync(
        EntityId patientId,
        EntityId relationshipAId,
        EntityId relationshipBId,
        PatientResponse winner)
    {
        await using var dbContext = CreateDbContext();
        var profile = await dbContext.PatientProfiles.AsNoTracking()
            .SingleAsync(value => value.Id == patientId);
        Assert.Equal(winner.BeeexyId, profile.BeeexyId.Value);
        Assert.Equal(winner.FirstName, profile.FirstName?.Value);
        Assert.Equal("Arias", profile.LastName?.Value);
        Assert.Equal(new DateOnly(2012, 5, 12), profile.DateOfBirth);
        Assert.Equal(SexAssignedAtBirth.Female, profile.SexAssignedAtBirth);
        Assert.Equal("FL", profile.State?.Code);
        Assert.Equal(3, profile.Version);
        Assert.Null(profile.AccountId);
        var relationships = await dbContext.CareRelationships.AsNoTracking()
            .Where(value => value.SubjectProfileId == patientId)
            .ToArrayAsync();
        Assert.Equal(2, relationships.Length);
        Assert.Equal(
            CareRelationshipStatus.Revoked,
            Assert.Single(relationships, value => value.Id == relationshipAId).Status);
        Assert.Equal(
            CareRelationshipStatus.Active,
            Assert.Single(relationships, value => value.Id == relationshipBId).Status);
    }

    private static async Task<AuthenticationResponse> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client,
        string prefix)
    {
        var email = $"phase-3-8-{prefix}-{Guid.NewGuid():N}@example.com";
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
        return Assert.IsType<AuthenticationResponse>(
            await verification.Content.ReadFromJsonAsync<AuthenticationResponse>());
    }

    private static async Task<CreateResponse> CreateManagedPatientAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            RelationshipsEndpoint,
            new
            {
                relationshipType = "Child",
                attestationVersion = "phase-3.8-race",
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
        return Assert.IsType<CreateResponse>(
            await response.Content.ReadFromJsonAsync<CreateResponse>());
    }

    private static async Task<AccessiblePatientsResponse> GetPatientsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(PatientsEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<AccessiblePatientsResponse>(
            await response.Content.ReadFromJsonAsync<AccessiblePatientsResponse>());
    }

    private static async Task<CareRelationshipsResponse> GetRelationshipsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(RelationshipsEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<CareRelationshipsResponse>(
            await response.Content.ReadFromJsonAsync<CareRelationshipsResponse>());
    }

    private static async Task<PatientResponse?> GetPatientAsync(
        HttpClient client,
        Guid patientId,
        HttpStatusCode expectedStatus)
    {
        using var response = await client.GetAsync(PatientEndpoint(patientId));
        Assert.Equal(expectedStatus, response.StatusCode);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<PatientResponse>()
            : null;
    }

    private static async Task<ProblemResponse> ReadProblemAsync(HttpResponseMessage response) =>
        Assert.IsType<ProblemResponse>(
            await response.Content.ReadFromJsonAsync<ProblemResponse>());

    private static void AssertPubliclyEquivalent(ProblemResponse expected, ProblemResponse actual)
    {
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Detail, actual.Detail);
    }

    private static string CreateJwt(
        Guid accountId,
        string issuer,
        string audience,
        DateTimeOffset notBefore,
        DateTimeOffset expires,
        string signingKey)
    {
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString("D")),
                new Claim("sid", Guid.NewGuid().ToString("D"))
            ],
            notBefore.UtcDateTime,
            expires.UtcDateTime,
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string PatientEndpoint(Guid id) => $"{PatientsEndpoint}/{id:D}";

    private static string RelationshipEndpoint(Guid id) => $"{RelationshipsEndpoint}/{id:D}";

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task<IdentityCounts> LoadIdentityCountsAsync()
    {
        await using var dbContext = CreateDbContext();
        return new IdentityCounts(
            await dbContext.Accounts.CountAsync(),
            await dbContext.ExternalIdentities.CountAsync(),
            await dbContext.RefreshSessions.CountAsync());
    }

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private sealed record AuthenticationResponse(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(Guid AccountId, Guid ProfileId, string BeeexyId);

    private sealed record CreateResponse(
        CreatedRelationshipResponse Relationship,
        CreatedPatientResponse Patient);

    private sealed record CreatedRelationshipResponse(
        Guid Id,
        string Type,
        string Status,
        string AttestationVersion,
        DateTimeOffset AttestedAt);

    private sealed record CreatedPatientResponse(
        Guid ProfileId,
        string BeeexyId,
        string FirstName,
        string LastName,
        DateOnly DateOfBirth,
        string SexAssignedAtBirth,
        string State,
        long Version);

    private sealed record PatientResponse(
        Guid ProfileId,
        string BeeexyId,
        string? FirstName,
        string? LastName,
        DateOnly? DateOfBirth,
        string? SexAssignedAtBirth,
        string? State,
        long Version);

    private sealed record AccessiblePatientsResponse(
        IReadOnlyList<AccessiblePatientResponse> Patients);

    private sealed record AccessiblePatientResponse(
        Guid ProfileId,
        string BeeexyId,
        string? FirstName,
        string? LastName,
        string AccessType,
        AccessibleRelationshipResponse? Relationship);

    private sealed record AccessibleRelationshipResponse(Guid RelationshipId, string Type);

    private sealed record CareRelationshipsResponse(
        IReadOnlyList<CareRelationshipResponse> Relationships);

    private sealed record CareRelationshipResponse(
        Guid Id,
        CareRelationshipSubjectResponse Subject,
        string Type,
        string Status,
        DateTimeOffset? RevokedAt);

    private sealed record CareRelationshipSubjectResponse(
        Guid ProfileId,
        string BeeexyId,
        string? FirstName,
        string? LastName);

    private sealed record ProblemResponse(
        int Status,
        string Title,
        string? Type,
        string? Detail);

    private sealed record IdentityCounts(int Accounts, int ExternalIdentities, int Sessions);
}
