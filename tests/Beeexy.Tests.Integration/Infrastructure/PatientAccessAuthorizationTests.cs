using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Patients;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Tests.Integration.Infrastructure;

[Collection(PostgreSqlCollection.Name)]
public sealed class PatientAccessAuthorizationTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task OwnPrimaryProfile_AuthorizesAsPrimary()
    {
        var account = await CreateAccountGraphAsync();

        var result = await AuthorizeAsync(account.Account.Id, account.Profile.Id);

        Assert.True(result.IsAuthorized);
        Assert.Equal(PatientAccessReason.Primary, result.Reason);
        Assert.Null(result.RelationshipId);
    }

    [Fact]
    public async Task AnotherAccountsPrimaryProfile_IsDenied()
    {
        var first = await CreateAccountGraphAsync();
        var second = await CreateAccountGraphAsync();

        var result = await AuthorizeAsync(first.Account.Id, second.Profile.Id);

        AssertDenied(result);
    }

    [Fact]
    public async Task ActiveManagerRelationship_AuthorizesAsManaged()
    {
        var manager = await CreateAccountGraphAsync();
        var subject = await CreateUnownedProfileAsync();
        var relationship = await CreateRelationshipAsync(manager, subject);

        var result = await AuthorizeAsync(manager.Account.Id, subject.Id);

        Assert.True(result.IsAuthorized);
        Assert.Equal(PatientAccessReason.Managed, result.Reason);
        Assert.Equal(relationship.Id, result.RelationshipId);
    }

    [Fact]
    public async Task RevokedRelationship_DeniesManagedAccess()
    {
        var manager = await CreateAccountGraphAsync();
        var subject = await CreateUnownedProfileAsync();
        var relationship = await CreateRelationshipAsync(manager, subject);
        await RevokeRelationshipAsync(relationship.Id, manager.Account.Id);

        var result = await AuthorizeAsync(manager.Account.Id, subject.Id);

        AssertDenied(result);
    }

    [Fact]
    public async Task MultipleManagers_AuthorizeIndependentlyAndSingleRevocationIsIsolated()
    {
        var firstManager = await CreateAccountGraphAsync();
        var secondManager = await CreateAccountGraphAsync();
        var subject = await CreateUnownedProfileAsync();
        var firstRelationship = await CreateRelationshipAsync(firstManager, subject);
        var secondRelationship = await CreateRelationshipAsync(secondManager, subject);

        var firstBefore = await AuthorizeAsync(firstManager.Account.Id, subject.Id);
        var secondBefore = await AuthorizeAsync(secondManager.Account.Id, subject.Id);
        await RevokeRelationshipAsync(firstRelationship.Id, firstManager.Account.Id);
        var firstAfter = await AuthorizeAsync(firstManager.Account.Id, subject.Id);
        var secondAfter = await AuthorizeAsync(secondManager.Account.Id, subject.Id);

        Assert.Equal(PatientAccessReason.Managed, firstBefore.Reason);
        Assert.Equal(firstRelationship.Id, firstBefore.RelationshipId);
        Assert.Equal(PatientAccessReason.Managed, secondBefore.Reason);
        Assert.Equal(secondRelationship.Id, secondBefore.RelationshipId);
        AssertDenied(firstAfter);
        Assert.Equal(PatientAccessReason.Managed, secondAfter.Reason);
        Assert.Equal(secondRelationship.Id, secondAfter.RelationshipId);
    }

    [Fact]
    public async Task RelationshipSubject_DoesNotGainAuthorityOverManager()
    {
        var manager = await CreateAccountGraphAsync();
        var subjectAccount = await CreateAccountGraphAsync();
        await CreateRelationshipAsync(manager, subjectAccount.Profile);

        var subjectToManager = await AuthorizeAsync(
            subjectAccount.Account.Id,
            manager.Profile.Id);
        var managerToSubject = await AuthorizeAsync(
            manager.Account.Id,
            subjectAccount.Profile.Id);

        AssertDenied(subjectToManager);
        Assert.Equal(PatientAccessReason.Managed, managerToSubject.Reason);
    }

    [Fact]
    public async Task UnknownTarget_IsDeniedWithSameConcealableResultAsUnauthorizedTarget()
    {
        var account = await CreateAccountGraphAsync();
        var unrelated = await CreateUnownedProfileAsync();
        var audit = new CapturingMyCircleAuditLogger();

        var missing = await AuthorizeAsync(account.Account.Id, EntityId.New(), audit);
        var unauthorized = await AuthorizeAsync(account.Account.Id, unrelated.Id, audit);

        AssertDenied(missing);
        AssertDenied(unauthorized);
        Assert.Equal(missing, unauthorized);
        Assert.Equal(
            new[]
            {
                PatientAccessDenialCategory.TargetNotFound,
                PatientAccessDenialCategory.NoActiveManagementRelationship
            },
            audit.Denials.Select(value => value.Category));
    }

    [Fact]
    public async Task ResourceAndRelationshipIdentifierKnowledge_DoesNotGrantAuthority()
    {
        var first = await CreateAccountGraphAsync();
        var second = await CreateAccountGraphAsync();
        var subject = await CreateUnownedProfileAsync();
        var relationship = await CreateRelationshipAsync(
            second,
            subject,
            createdByAccountId: first.Account.Id);

        var knownPatient = await AuthorizeAsync(first.Account.Id, subject.Id);
        var relationshipIdAsTarget = await AuthorizeAsync(first.Account.Id, relationship.Id);
        var creatorIdAsTarget = await AuthorizeAsync(first.Account.Id, first.Account.Id);

        AssertDenied(knownPatient);
        AssertDenied(relationshipIdAsTarget);
        AssertDenied(creatorIdAsTarget);
        var executeMethods = typeof(AuthorizePatientAccess)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == nameof(AuthorizePatientAccess.ExecuteAsync))
            .ToArray();
        var executeMethod = Assert.Single(executeMethods);
        Assert.Equal(typeof(EntityId), executeMethod.GetParameters()[0].ParameterType);
    }

    [Fact]
    public async Task Phase32CreatedManagedPatient_ImmediatelyAuthorizesAsManaged()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client);
        SetBearer(client, authentication.AccessToken);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/care-relationships",
            new
            {
                relationshipType = "Child",
                attestationVersion = "phase-3.4-e2e",
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
        var created = await response.Content.ReadFromJsonAsync<CreateResponse>();
        Assert.NotNull(created);

        var result = await AuthorizeAsync(
            EntityId.From(authentication.Account.AccountId),
            EntityId.From(created.Patient.ProfileId));

        Assert.Equal(PatientAccessReason.Managed, result.Reason);
        Assert.Equal(EntityId.From(created.Relationship.Id), result.RelationshipId);
    }

    [Fact]
    public async Task Phase33PatientListing_AgreesWithPrimaryManagedAndRevokedAuthorization()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client);
        SetBearer(client, authentication.AccessToken);
        var graph = await LoadAccountGraphAsync(authentication.Account.AccountId);
        var activeSubject = await CreateUnownedProfileAsync();
        var revokedSubject = await CreateUnownedProfileAsync();
        await CreateRelationshipAsync(graph, activeSubject);
        var revoked = await CreateRelationshipAsync(graph, revokedSubject);
        await RevokeRelationshipAsync(revoked.Id, graph.Account.Id);

        using var response = await client.GetAsync("/api/v1/patients");
        response.EnsureSuccessStatusCode();
        var patients = await response.Content.ReadFromJsonAsync<AccessiblePatientsResponse>();
        Assert.NotNull(patients);
        var primaryAuthorization = await AuthorizeAsync(graph.Account.Id, graph.Profile.Id);
        var activeAuthorization = await AuthorizeAsync(graph.Account.Id, activeSubject.Id);
        var revokedAuthorization = await AuthorizeAsync(graph.Account.Id, revokedSubject.Id);

        Assert.Equal("Primary", Assert.Single(
            patients.Patients,
            value => value.ProfileId == graph.Profile.Id.Value).AccessType);
        Assert.Equal("Managed", Assert.Single(
            patients.Patients,
            value => value.ProfileId == activeSubject.Id.Value).AccessType);
        Assert.DoesNotContain(
            patients.Patients,
            value => value.ProfileId == revokedSubject.Id.Value);
        Assert.Equal(PatientAccessReason.Primary, primaryAuthorization.Reason);
        Assert.Equal(PatientAccessReason.Managed, activeAuthorization.Reason);
        AssertDenied(revokedAuthorization);
    }

    [Fact]
    public async Task OpenApi_KeepsAuthorizationInternalWithPatientGetAndPatch()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        Assert.Equal(19, paths.EnumerateObject().Count());
        Assert.False(paths.TryGetProperty("/api/v1/patient-access", out _));
        var patientDetail = paths.GetProperty("/api/v1/patients/{patientId}");
        Assert.True(patientDetail.TryGetProperty("get", out _));
        Assert.True(patientDetail.TryGetProperty("patch", out _));
        Assert.True(paths
            .GetProperty("/api/v1/care-relationships/{id}")
            .TryGetProperty("delete", out _));
    }

    private async Task<PatientAccessAuthorizationResult> AuthorizeAsync(
        EntityId accountId,
        EntityId targetProfileId,
        CapturingMyCircleAuditLogger? auditLogger = null)
    {
        await using var dbContext = CreateDbContext();
        var profileAuditLogger = new CapturingAccountProfileAuditLogger();
        var resolver = new CurrentAccountProfileResolver(
            new FakeCurrentSessionIdentity(accountId),
            new CurrentAccountProfileRepository(dbContext),
            profileAuditLogger);
        var useCase = new AuthorizePatientAccess(
            new FixedClock(),
            resolver,
            new PatientAccessAuthorizationRepository(dbContext),
            auditLogger ?? new CapturingMyCircleAuditLogger());
        return await useCase.ExecuteAsync(targetProfileId);
    }

    private async Task<AccountGraph> CreateAccountGraphAsync()
    {
        await EnsureMigratedAsync();
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");
        var account = Account.Create(
            NormalizedEmail.Create($"patient-access-{suffix}@example.com"),
            now);
        var profile = PatientProfile.Create(
            BeeexyId.Create($"BXY-{suffix}".ToUpperInvariant()),
            now,
            account.Id);
        var preference = UserPreference.Create(
            account.Id,
            UserTimeZone.Create("Etc/UTC"),
            now);
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(account, profile, preference);
        await dbContext.SaveChangesAsync();
        return new AccountGraph(account, profile);
    }

    private async Task<AccountGraph> LoadAccountGraphAsync(Guid accountId)
    {
        await using var dbContext = CreateDbContext();
        var id = EntityId.From(accountId);
        var account = await dbContext.Accounts
            .AsNoTracking()
            .SingleAsync(value => value.Id == id);
        var profile = await dbContext.PatientProfiles
            .AsNoTracking()
            .SingleAsync(value => value.AccountId == id);
        return new AccountGraph(account, profile);
    }

    private async Task<PatientProfile> CreateUnownedProfileAsync()
    {
        await EnsureMigratedAsync();
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");
        var profile = PatientProfile.Create(
            BeeexyId.Create($"BXY-{suffix}".ToUpperInvariant()),
            now);
        await using var dbContext = CreateDbContext();
        dbContext.PatientProfiles.Add(profile);
        await dbContext.SaveChangesAsync();
        return profile;
    }

    private async Task<CareRelationship> CreateRelationshipAsync(
        AccountGraph manager,
        PatientProfile subject,
        EntityId? createdByAccountId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var relationship = CareRelationship.Create(
            manager.Profile.Id,
            subject.Id,
            CareRelationshipType.Caregiver,
            createdByAccountId ?? manager.Account.Id,
            AuthorizationAttestation.Create("phase-3.4-test", now),
            now);
        await using var dbContext = CreateDbContext();
        dbContext.CareRelationships.Add(relationship);
        await dbContext.SaveChangesAsync();
        return relationship;
    }

    private async Task RevokeRelationshipAsync(
        EntityId relationshipId,
        EntityId revokedByAccountId)
    {
        await using var dbContext = CreateDbContext();
        var relationship = await dbContext.CareRelationships
            .SingleAsync(value => value.Id == relationshipId);
        relationship.Revoke(revokedByAccountId, DateTimeOffset.UtcNow.AddSeconds(1));
        await dbContext.SaveChangesAsync();
    }

    private async Task<AuthenticationResult> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client)
    {
        var email = $"patient-access-api-{Guid.NewGuid():N}@example.com";
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

    private static void AssertDenied(PatientAccessAuthorizationResult result)
    {
        Assert.False(result.IsAuthorized);
        Assert.Equal(PatientAccessReason.Denied, result.Reason);
        Assert.Null(result.RelationshipId);
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

    private sealed class FakeCurrentSessionIdentity(EntityId accountId)
        : ICurrentSessionIdentity
    {
        public CurrentSessionIdentity GetRequired() => new(accountId, EntityId.New());
    }

    private sealed class CapturingAccountProfileAuditLogger : IAccountProfileAuditLogger
    {
        public void InvariantViolation(EntityId accountId, string invariant)
        {
        }

        public void ProfileUpdateSucceeded(
            EntityId accountId,
            EntityId profileId,
            IReadOnlyCollection<string> changedFields,
            DateTimeOffset occurredAt)
        {
        }

        public void ProfileUpdateConflict(EntityId accountId, EntityId profileId)
        {
        }
    }

    private sealed class CapturingMyCircleAuditLogger : IMyCircleAuditLogger
    {
        public List<DenialEvent> Denials { get; } = [];

        public void DuplicateAccessiblePatientDetected(
            EntityId accountId,
            EntityId managerProfileId,
            EntityId subjectProfileId)
        {
        }

        public void PatientAccessDenied(
            EntityId accountId,
            EntityId managerProfileId,
            EntityId targetProfileId,
            PatientAccessDenialCategory category,
            DateTimeOffset occurredAt) => Denials.Add(new DenialEvent(
                accountId,
                managerProfileId,
                targetProfileId,
                category,
                occurredAt));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow =>
            new(2026, 8, 20, 21, 0, 0, TimeSpan.Zero);
    }

    private sealed record AccountGraph(Account Account, PatientProfile Profile);

    private sealed record DenialEvent(
        EntityId AccountId,
        EntityId ManagerProfileId,
        EntityId TargetProfileId,
        PatientAccessDenialCategory Category,
        DateTimeOffset OccurredAt);

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

    private sealed record CreatedPatientResponse(Guid ProfileId);

    private sealed record AccessiblePatientsResponse(
        IReadOnlyList<AccessiblePatientResponse> Patients);

    private sealed record AccessiblePatientResponse(Guid ProfileId, string AccessType);
}
