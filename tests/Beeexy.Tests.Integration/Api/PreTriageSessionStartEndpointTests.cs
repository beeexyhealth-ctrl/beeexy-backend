using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class PreTriageSessionStartEndpointTests(
    PostgreSqlContainerFixture postgres) : IAsyncLifetime
{
    private const string Endpoint = "/api/v1/pre-triage/sessions";
    private const string SigningKey =
        "integration-test-only-jwt-signing-key-with-at-least-32-bytes";
    private const string Issuer = "https://api.beeexy.com";
    private const string Audience = "beeexy-client";
    private const string VersionTestSource = "phase-4.4-version-integrity-test";
    private EntityId[] _preexistingSessionIds = [];

    [Fact]
    public async Task AnonymousStart_ReturnsCapabilityOnceAndPersistsOnlyHashAndTemporaryState()
    {
        using var logger = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: logger);
        using var client = factory.CreateApiClient();
        var before = DateTimeOffset.UtcNow;
        var permanentCountsBefore = await LoadPermanentCountsAsync();

        using var firstResponse = await client.PostAsJsonAsync(
            Endpoint,
            new { pathway = "ABDOMINAL_PAIN" });
        using var secondResponse = await client.PostAsJsonAsync(
            Endpoint,
            new { pathway = "ABDOMINAL_PAIN" });
        var after = DateTimeOffset.UtcNow;
        var firstJson = await firstResponse.Content.ReadAsStringAsync();
        var first = JsonSerializer.Deserialize<StartResponse>(
            firstJson,
            JsonOptions)!;
        var second = await secondResponse.Content.ReadFromJsonAsync<StartResponse>();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.NotNull(second);
        Assert.Equal("ABDOMINAL_PAIN", first.Pathway);
        Assert.Equal("Active", first.Status);
        Assert.Null(first.PatientId);
        Assert.NotNull(first.AnonymousCapability);
        Assert.NotNull(second.AnonymousCapability);
        Assert.NotEqual(first.AnonymousCapability, second.AnonymousCapability);
        Assert.Null(firstResponse.Headers.Location);
        Assert.InRange(
            first.ExpiresAt,
            before.AddHours(24),
            after.AddHours(24));
        var expectedPackage = SimplifiedDemoDefinitionPackages.Create(
            ClinicalPathways.AbdominalPain);
        Assert.Equal(
            expectedPackage.Questionnaire.QuestionnaireCode.Value,
            first.Questionnaire.Code);
        Assert.Equal(
            SimplifiedDemoDefinitionPackages.VersionIdentifier,
            first.Questionnaire.Version);
        Assert.Equal(
            expectedPackage.RuleSet.RuleSetCode.Value,
            first.RuleSet.Code);
        Assert.Equal(first.Questionnaire.Version, first.RuleSet.Version);
        Assert.Equal("PRODUCT_DEMO_DEFINED", first.ClinicalContent.Source);
        Assert.Equal("NOT_APPLICABLE", first.ClinicalContent.ReviewStatus);
        Assert.Equal(
            "NOT_CLINICALLY_APPROVED",
            first.ClinicalContent.ClinicalApproval);

        await using (var dbContext = CreateDbContext())
        {
            var sessions = await dbContext.PreTriageSessions
                .AsNoTracking()
                .Where(value => value.Id == EntityId.From(first.SessionId) ||
                    value.Id == EntityId.From(second.SessionId))
                .OrderBy(value => value.CreatedAt)
                .ToArrayAsync();
            Assert.Equal(2, sessions.Length);
            Assert.All(sessions, session =>
            {
                Assert.Null(session.PatientProfileId);
                Assert.NotNull(session.AnonymousCapabilityHash);
                Assert.Equal(PreTriageSessionStatus.Active, session.Status);
            });
            Assert.DoesNotContain(
                sessions,
                session => session.AnonymousCapabilityHash!.Value == first.AnonymousCapability);
            Assert.NotEqual(
                sessions[0].AnonymousCapabilityHash,
                sessions[1].AnonymousCapabilityHash);
            var capabilityService = new CryptographicAnonymousPreTriageCapabilityService();
            Assert.True(capabilityService.Verify(
                first.AnonymousCapability,
                sessions.Single(value => value.Id == EntityId.From(first.SessionId))
                    .AnonymousCapabilityHash!));
        }

        Assert.Equal(permanentCountsBefore, await LoadPermanentCountsAsync());
        var logs = string.Join('\n', logger.Messages);
        Assert.DoesNotContain(first.AnonymousCapability, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(
            new CryptographicAnonymousPreTriageCapabilityService()
                .Hash(first.AnonymousCapability).Value,
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(first.AnonymousCapability, firstResponse.Headers.ToString());
    }

    [Fact]
    public async Task AuthenticatedPrimaryStart_DefaultsToPrimaryAndNeverReturnsCapability()
    {
        var identity = await CreateIdentityAsync("primary-start");
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        SetBearer(client, identity.Token);

        using var inferredResponse = await client.PostAsJsonAsync(
            Endpoint,
            new { pathway = "ABDOMINAL_PAIN" });
        using var explicitResponse = await client.PostAsJsonAsync(
            Endpoint,
            new
            {
                pathway = "ABDOMINAL_PAIN",
                patientId = identity.ProfileId.Value
            });
        var inferredJson = await inferredResponse.Content.ReadAsStringAsync();
        var explicitJson = await explicitResponse.Content.ReadAsStringAsync();
        var inferred = JsonSerializer.Deserialize<StartResponse>(inferredJson, JsonOptions)!;
        var explicitResult = JsonSerializer.Deserialize<StartResponse>(explicitJson, JsonOptions)!;

        Assert.Equal(HttpStatusCode.Created, inferredResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, explicitResponse.StatusCode);
        Assert.Equal(identity.ProfileId.Value, inferred.PatientId);
        Assert.Equal(identity.ProfileId.Value, explicitResult.PatientId);
        Assert.Null(inferred.AnonymousCapability);
        Assert.Null(explicitResult.AnonymousCapability);
        Assert.DoesNotContain("anonymousCapability", inferredJson, StringComparison.Ordinal);
        Assert.DoesNotContain("anonymousCapability", explicitJson, StringComparison.Ordinal);

        await using var dbContext = CreateDbContext();
        var sessions = await dbContext.PreTriageSessions
            .AsNoTracking()
            .Where(value => value.Id == EntityId.From(inferred.SessionId) ||
                value.Id == EntityId.From(explicitResult.SessionId))
            .ToArrayAsync();
        Assert.Equal(2, sessions.Length);
        Assert.All(sessions, session =>
        {
            Assert.Equal(identity.ProfileId, session.PatientProfileId);
            Assert.Null(session.AnonymousCapabilityHash);
        });
    }

    [Fact]
    public async Task TwoActiveManagers_CanIndependentlyStartForTheSameManagedPatient()
    {
        var managerA = await CreateIdentityAsync("manager-a");
        var managerB = await CreateIdentityAsync("manager-b");
        var managed = await CreateManagedPatientAsync("shared-subject");
        await CreateRelationshipAsync(managerA, managed.Id);
        await CreateRelationshipAsync(managerB, managed.Id);
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var clientA = factory.CreateApiClient();
        using var clientB = factory.CreateApiClient();
        SetBearer(clientA, managerA.Token);
        SetBearer(clientB, managerB.Token);

        using var responseA = await clientA.PostAsJsonAsync(
            Endpoint,
            new { pathway = "ABDOMINAL_PAIN", patientId = managed.Id.Value });
        using var responseB = await clientB.PostAsJsonAsync(
            Endpoint,
            new { pathway = "ABDOMINAL_PAIN", patientId = managed.Id.Value });
        var resultA = await responseA.Content.ReadFromJsonAsync<StartResponse>();
        var resultB = await responseB.Content.ReadFromJsonAsync<StartResponse>();

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
        Assert.Equal(managed.Id.Value, resultA!.PatientId);
        Assert.Equal(managed.Id.Value, resultB!.PatientId);
        Assert.Null(resultA.AnonymousCapability);
        Assert.Null(resultB.AnonymousCapability);
    }

    [Fact]
    public async Task UnauthorizedRevokedReverseAndMissingPatients_AllReceiveConcealedNotFound()
    {
        var caller = await CreateIdentityAsync("denied-manager");
        var unrelated = await CreateManagedPatientAsync("unrelated");
        var revoked = await CreateManagedPatientAsync("revoked");
        var reverseManager = await CreateManagedPatientAsync("reverse-manager");
        var revokedRelationship = await CreateRelationshipAsync(caller, revoked.Id);
        await RevokeRelationshipAsync(revokedRelationship, caller.AccountId);
        await CreateRelationshipAsync(
            new TestIdentity(caller.AccountId, reverseManager.Id, caller.Token),
            caller.ProfileId);
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        SetBearer(client, caller.Token);
        var before = await CountSessionsAsync();

        foreach (var patientId in new[]
                 {
                     unrelated.Id.Value,
                     revoked.Id.Value,
                     reverseManager.Id.Value,
                     Guid.NewGuid()
                 })
        {
            using var response = await client.PostAsJsonAsync(
                Endpoint,
                new { pathway = "ABDOMINAL_PAIN", patientId });
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(
                "Patient profile not found.",
                problem.RootElement.GetProperty("title").GetString());
        }

        Assert.Equal(before, await CountSessionsAsync());
    }

    [Fact]
    public async Task InvalidSuppliedBearerMatrix_NeverDowngradesToAnonymous()
    {
        var identity = await CreateIdentityAsync("jwt-matrix");
        var disabled = await CreateIdentityAsync("disabled", disabled: true);
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var now = DateTimeOffset.UtcNow;

        using (var anonymous = await client.PostAsJsonAsync(
                   Endpoint,
                   new { pathway = "ABDOMINAL_PAIN" }))
        {
            Assert.Equal(HttpStatusCode.Created, anonymous.StatusCode);
        }

        var invalidCredentials = new[]
        {
            "not-a-valid-jwt",
            CreateJwt(identity.AccountId, Issuer, Audience, now, now.AddMinutes(5),
                "wrong-signing-key-with-at-least-thirty-two-bytes"),
            CreateJwt(identity.AccountId, "https://wrong-issuer.example", Audience, now,
                now.AddMinutes(5), SigningKey),
            CreateJwt(identity.AccountId, Issuer, "wrong-audience", now, now.AddMinutes(5),
                SigningKey),
            CreateJwt(identity.AccountId, Issuer, Audience, now.AddMinutes(-5),
                now.AddMinutes(-1), SigningKey),
            disabled.Token
        };
        var afterAnonymous = await CountSessionsAsync();
        foreach (var credential in invalidCredentials)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = JsonContent.Create(new { pathway = "ABDOMINAL_PAIN" })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var basicRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { pathway = "ABDOMINAL_PAIN" })
        })
        {
            basicRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", "abc");
            using var basicResponse = await client.SendAsync(basicRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, basicResponse.StatusCode);
        }

        Assert.Equal(afterAnonymous, await CountSessionsAsync());
    }

    [Theory]
    [InlineData("CHEST_PAIN")]
    [InlineData("RESPIRATORY_SYMPTOMS")]
    [InlineData("BACK_PAIN")]
    [InlineData("OTHER_SYMPTOMS")]
    public async Task RecognizedUnsupportedPathway_Returns422WithoutSession(
        string pathway)
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var before = await CountSessionsAsync();

        using var response = await client.PostAsJsonAsync(Endpoint, new { pathway });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(
            "pre_triage.pathway_unsupported",
            problem.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(before, await CountSessionsAsync());
    }

    [Theory]
    [InlineData("HEADACHE")]
    [InlineData("ABDOMINAL_PAIN")]
    [InlineData("FEVER")]
    public async Task ConfirmedDemoPathway_StartsAndPinsItsSimplifiedPackage(string pathway)
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(Endpoint, new { pathway });
        var result = await response.Content.ReadFromJsonAsync<StartResponse>();
        var expected = SimplifiedDemoDefinitionPackages.Create(
            ClinicalPathwayCode.Create(pathway));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(expected.Questionnaire.QuestionnaireCode.Value,
            result.Questionnaire.Code);
        Assert.Equal(expected.Version.Value, result.Questionnaire.Version);
        Assert.Equal(expected.RuleSet.RuleSetCode.Value, result.RuleSet.Code);
        Assert.Equal("PRODUCT_DEMO_DEFINED", result.ClinicalContent.Source);
    }

    [Fact]
    public async Task UnknownPathway_IsDistinctAndNeverLoadsAbdominalDefinition()
    {
        const string unknownPathway = "My stomach hurts after dinner.";
        using var logger = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: logger);
        using var client = factory.CreateApiClient();
        var before = await CountSessionsAsync();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { pathway = unknownPathway });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(
            "pre_triage.pathway_unknown",
            problem.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(before, await CountSessionsAsync());
        Assert.DoesNotContain(
            unknownPathway,
            string.Join('\n', logger.Messages),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SupportedPathwayWithoutUsableDefinition_FailsClosedWithoutCapability()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<IClinicalPathwayRegistry>();
                services.AddSingleton<IClinicalPathwayRegistry, NoDefinitionRegistry>();
            });
        using var client = factory.CreateApiClient();
        var before = await CountSessionsAsync();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { pathway = "ABDOMINAL_PAIN" });
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(
            "pre_triage.definition_unavailable",
            problem.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("anonymousCapability", body, StringComparison.Ordinal);
        Assert.Equal(before, await CountSessionsAsync());
    }

    [Fact]
    public async Task MalformedUnsupportedAndAnonymousPatientRequests_FollowSafeErrorContract()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var before = await CountSessionsAsync();

        using var malformed = await client.PostAsync(
            Endpoint,
            new StringContent("{", Encoding.UTF8, "application/json"));
        using var unsupported = await client.PostAsJsonAsync(
            Endpoint,
            new { pathway = "ABDOMINAL_PAIN", urgency = "HIGH" });
        using var anonymousPatient = await client.PostAsJsonAsync(
            Endpoint,
            new { pathway = "ABDOMINAL_PAIN", patientId = Guid.NewGuid() });
        using var naturalLanguage = await client.PostAsJsonAsync(
            Endpoint,
            new { initialInput = "My stomach hurts." });

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsupported.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousPatient.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, naturalLanguage.StatusCode);
        Assert.Equal(before, await CountSessionsAsync());
    }

    [Fact]
    public async Task SessionPinsExactActiveDefinitionWhenLaterActiveVersionChanges()
    {
        var versionTwo = await ImportTestVersionAsync("phase44-test-v2", 2);
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { pathway = "ABDOMINAL_PAIN" });
        var result = await response.Content.ReadFromJsonAsync<StartResponse>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(versionTwo.Questionnaire.Version.Value, result.Questionnaire.Version);
        Assert.Equal(versionTwo.RuleSet.Version.Value, result.RuleSet.Version);

        await ImportTestVersionAsync("phase44-test-v3", 3);
        await using var dbContext = CreateDbContext();
        var persisted = await dbContext.PreTriageSessions
            .AsNoTracking()
            .SingleAsync(value => value.Id == EntityId.From(result.SessionId));
        Assert.Equal(versionTwo.Questionnaire.Id, persisted.QuestionnaireVersionId);
        Assert.NotEqual(
            (await dbContext.QuestionnaireVersions
                .AsNoTracking()
                .Where(value => value.SourceReference == VersionTestSource)
                .OrderByDescending(value => value.ActivatedAt)
                .FirstAsync()).Id,
            persisted.QuestionnaireVersionId);
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        _preexistingSessionIds = await dbContext.PreTriageSessions
            .AsNoTracking()
            .Select(value => value.Id)
            .ToArrayAsync();
        var importer = new ClinicalDefinitionImporter(
            dbContext,
            new ClinicalDefinitionPackageValidator(),
            NullLogger<ClinicalDefinitionImporter>.Instance);
        await importer.ImportAsync(AbdominalPainProvisionalPackage.Create());
        foreach (var package in SimplifiedDemoDefinitionPackages.CreateAll())
        {
            await importer.ImportAsync(package);
        }
    }

    public async Task DisposeAsync()
    {
        await using var dbContext = CreateDbContext();
        await DeleteCreatedSessionsAndTestVersionsAsync(dbContext);
    }

    private async Task<ClinicalDefinitionPackage> ImportTestVersionAsync(
        string versionValue,
        int activationDayOffset)
    {
        var original = SimplifiedDemoDefinitionPackages.Create(
            ClinicalPathways.AbdominalPain);
        var version = DefinitionVersion.Create(versionValue);
        var importedAt = original.Questionnaire.ImportedAt.AddDays(activationDayOffset);
        var questionnaire = QuestionnaireDefinitionVersion.Import(
            original.Pathway,
            original.Questionnaire.QuestionnaireCode,
            version,
            original.Questionnaire.ContentHash,
            original.ContentStatus,
            importedAt,
            activatedAt: importedAt,
            sourceReference: VersionTestSource,
            questions: original.Questionnaire.Questions.Select(question =>
                new TriageQuestionInput(
                    question.Code,
                    question.PromptText,
                    question.DisplayOrder,
                    question.AnswerSchemaJson,
                    question.BranchingMetadataJson)));
        var ruleSet = ClinicalRuleSetVersion.Import(
            original.Pathway,
            original.RuleSet.RuleSetCode,
            version,
            original.RuleSet.ContentHash,
            original.ContentStatus,
            original.RuleSet.DefinitionMetadataJson,
            importedAt,
            activatedAt: importedAt,
            sourceReference: VersionTestSource);
        var package = new ClinicalDefinitionPackage(
            original.Pathway,
            questionnaire,
            ruleSet,
            original.Questions,
            original.Branches,
            original.RuleDefinitions);
        await using var dbContext = CreateDbContext();
        await new ClinicalDefinitionImporter(
                dbContext,
                new ClinicalDefinitionPackageValidator(),
                NullLogger<ClinicalDefinitionImporter>.Instance)
            .ImportAsync(package);
        return package;
    }

    private async Task<TestIdentity> CreateIdentityAsync(string suffix, bool disabled = false)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var account = Account.Create(
            NormalizedEmail.Create($"phase44-{suffix}-{Guid.NewGuid():N}@example.com"),
            now);
        var profile = PatientProfile.Create(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            now,
            account.Id);
        var preference = UserPreference.Create(
            account.Id,
            UserTimeZone.Create("Etc/UTC"),
            now);
        if (disabled)
        {
            account.Disable(now.AddSeconds(1));
        }

        await using (var dbContext = CreateDbContext())
        {
            dbContext.AddRange(account, profile, preference);
            await dbContext.SaveChangesAsync();
        }

        var token = CreateJwt(
            account.Id,
            Issuer,
            Audience,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(10),
            SigningKey);
        return new TestIdentity(account.Id, profile.Id, token);
    }

    private async Task<PatientProfile> CreateManagedPatientAsync(string suffix)
    {
        var now = DateTimeOffset.UtcNow;
        var patient = PatientProfile.CreateManaged(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}".ToUpperInvariant()),
            PatientName.Create("Phase"),
            PatientName.Create(suffix),
            new DateOnly(2012, 1, 1),
            SexAssignedAtBirth.Female,
            UsState.Create("NY"),
            now);
        await using var dbContext = CreateDbContext();
        dbContext.PatientProfiles.Add(patient);
        await dbContext.SaveChangesAsync();
        return patient;
    }

    private async Task<CareRelationship> CreateRelationshipAsync(
        TestIdentity manager,
        EntityId subjectId)
    {
        var now = DateTimeOffset.UtcNow;
        var relationship = CareRelationship.Create(
            manager.ProfileId,
            subjectId,
            CareRelationshipType.Caregiver,
            manager.AccountId,
            AuthorizationAttestation.Create("phase-4.4-test", now),
            now);
        await using var dbContext = CreateDbContext();
        dbContext.CareRelationships.Add(relationship);
        await dbContext.SaveChangesAsync();
        return relationship;
    }

    private async Task RevokeRelationshipAsync(
        CareRelationship relationship,
        EntityId accountId)
    {
        await using var dbContext = CreateDbContext();
        var persisted = await dbContext.CareRelationships.SingleAsync(
            value => value.Id == relationship.Id);
        persisted.Revoke(accountId, DateTimeOffset.UtcNow.AddSeconds(1));
        await dbContext.SaveChangesAsync();
    }

    private async Task<(int Episodes, int Assessments, int Findings)> LoadPermanentCountsAsync()
    {
        await using var dbContext = CreateDbContext();
        return (
            await dbContext.PreTriageEpisodes.CountAsync(),
            await dbContext.ClinicalAssessments.CountAsync(),
            await dbContext.ClinicalFindings.CountAsync());
    }

    private async Task<int> CountSessionsAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.PreTriageSessions.CountAsync();
    }

    private async Task DeleteCreatedSessionsAndTestVersionsAsync(BeeexyDbContext dbContext)
    {
        await dbContext.PreTriageSessions
            .Where(value => !_preexistingSessionIds.Contains(value.Id))
            .ExecuteDeleteAsync();
        var testQuestionnaireIds = await dbContext.QuestionnaireVersions
            .Where(value => value.SourceReference == VersionTestSource)
            .Select(value => value.Id)
            .ToArrayAsync();
        if (testQuestionnaireIds.Length > 0)
        {
            await dbContext.TriageQuestions
                .Where(value => testQuestionnaireIds.Contains(value.QuestionnaireVersionId))
                .ExecuteDeleteAsync();
            await dbContext.QuestionnaireVersions
                .Where(value => value.SourceReference == VersionTestSource)
                .ExecuteDeleteAsync();
            await dbContext.ClinicalRuleSetVersions
                .Where(value => value.SourceReference == VersionTestSource)
                .ExecuteDeleteAsync();
        }
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private static string CreateJwt(
        EntityId accountId,
        string issuer,
        string audience,
        DateTimeOffset notBefore,
        DateTimeOffset expiresAt,
        string signingKey)
    {
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, accountId.Value.ToString("D")),
                new Claim("sid", Guid.NewGuid().ToString("D"))
            ],
            notBefore.UtcDateTime,
            expiresAt.UtcDateTime,
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);

    private sealed class NoDefinitionRegistry : IClinicalPathwayRegistry
    {
        public bool IsRecognized(ClinicalPathwayCode pathway) => true;

        public bool IsSupported(ClinicalPathwayCode pathway) => true;

        public Task<ClinicalPathwayResolution> ResolveAsync(
            string pathwayCode,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new ClinicalPathwayResolution(
                    ClinicalPathwayResolutionStatus.Supported,
                    ClinicalPathways.AbdominalPain,
                    null));
    }

    private sealed record TestIdentity(
        EntityId AccountId,
        EntityId ProfileId,
        string Token);

    private sealed record StartResponse(
        Guid SessionId,
        Guid? PatientId,
        string Pathway,
        string Status,
        DateTimeOffset ExpiresAt,
        DefinitionReference Questionnaire,
        DefinitionReference RuleSet,
        ContentStatusResponse ClinicalContent,
        string? AnonymousCapability);

    private sealed record DefinitionReference(string Code, string Version);

    private sealed record ContentStatusResponse(
        string Source,
        string ReviewStatus,
        string ClinicalApproval);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
