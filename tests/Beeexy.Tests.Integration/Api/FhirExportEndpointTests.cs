using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.Interoperability;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class FhirExportEndpointTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Owner_EndToEndJourneyReturnsExactValidatedStoredR4BytesAndSafeAudit()
    {
        await EnsureMigratedAsync();
        var store = new MutableArtifactStore();
        using var logs = new InMemoryLoggerProvider();
        using var factory = Factory(store, logs);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "fhir-owner");
        SetBearer(client, authentication.AccessToken);
        var source = await CompletePreTriageAsync(client, authentication.Account.ProfileId);
        var before = await SourceSnapshotAsync(source.EpisodeId);

        using var create = await client.PostAsJsonAsync(
            CreateEndpoint(authentication.Account.ProfileId),
            Request(source.EventId, Guid.NewGuid()));
        var metadata = await create.Content.ReadFromJsonAsync<ExportMetadata>();

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(metadata);
        Assert.Equal("Validated", metadata.Status);
        Assert.Equal(FhirR4BaseMvp.FhirRelease, metadata.FhirVersion);
        Assert.Equal(FhirR4BaseMvp.MappingVersion, metadata.MappingVersion);
        Assert.Equal("Passed", metadata.Validation!.Outcome);
        Assert.Equal(0, metadata.Validation.ErrorCount);
        var createBody = await create.Content.ReadAsStringAsync();
        Assert.DoesNotContain("resourceType", createBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checksum", createBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage", createBody, StringComparison.OrdinalIgnoreCase);

        using var get = await client.GetAsync(MetadataEndpoint(metadata.Id));
        var retrieved = await get.Content.ReadFromJsonAsync<ExportMetadata>();
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(metadata, retrieved);

        await using var db = CreateDbContext();
        var persisted = await db.FhirExports.AsNoTracking().SingleAsync(
            candidate => candidate.Id == EntityId.From(metadata.Id));
        var stored = store.Get(persisted.PrivateArtifactStorageUri!);
        using var download = await client.GetAsync(ContentEndpoint(metadata.Id));
        var downloaded = await download.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(FhirR4BaseMvp.MediaType,
            download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(stored, downloaded);
        Assert.Contains(metadata.Id.ToString("D"),
            download.Content.Headers.ContentDisposition!.FileName!);
        Assert.DoesNotContain(authentication.Account.BeeexyId,
            download.Content.Headers.ContentDisposition!.FileName!,
            StringComparison.Ordinal);
        using var document = JsonDocument.Parse(downloaded);
        Assert.Equal("Bundle", document.RootElement.GetProperty("resourceType").GetString());
        Assert.Equal("collection", document.RootElement.GetProperty("type").GetString());
        var entries = document.RootElement.GetProperty("entry").EnumerateArray().ToArray();
        Assert.Equal(
            ["QuestionnaireResponse", "Device", "Provenance"],
            entries.Select(entry => entry.GetProperty("resource")
                .GetProperty("resourceType").GetString()));
        Assert.All(entries, entry => Assert.StartsWith(
            "urn:uuid:", entry.GetProperty("fullUrl").GetString()));
        var questionnaireItems = entries[0].GetProperty("resource")
            .GetProperty("item").EnumerateArray()
            .ToDictionary(item => item.GetProperty("linkId").GetString()!);
        Assert.All(questionnaireItems.Keys, linkId => Assert.False(
            string.IsNullOrWhiteSpace(linkId)));
        var duration = Assert.Single(questionnaireItems["DURATION"]
            .GetProperty("answer").EnumerateArray());
        Assert.Equal(2, duration.GetProperty("valueQuantity")
            .GetProperty("value").GetDecimal());
        Assert.Equal("DAYS", duration.GetProperty("valueQuantity")
            .GetProperty("unit").GetString());
        var intensity = Assert.Single(questionnaireItems["INTENSITY"]
            .GetProperty("answer").EnumerateArray());
        Assert.Equal(7, intensity.GetProperty("valueInteger").GetInt32());
        Assert.Equal(["FEVER"], questionnaireItems["ADDITIONAL_SYMPTOMS"]
            .GetProperty("answer").EnumerateArray()
            .Select(answer => answer.GetProperty("valueString").GetString()));
        Assert.DoesNotContain("RiskAssessment", Encoding.UTF8.GetString(downloaded));
        Assert.DoesNotContain("Composition", Encoding.UTF8.GetString(downloaded));
        Assert.Equal(before, await SourceSnapshotAsync(source.EpisodeId));

        var logText = string.Join('\n', logs.Messages);
        Assert.Contains($"FHIR export {metadata.Id:D} created", logText);
        Assert.Contains($"FHIR export {metadata.Id:D} validation completed", logText);
        Assert.Contains($"FHIR export {metadata.Id:D} downloaded", logText);
        Assert.DoesNotContain("resourceType", logText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("beeexy-private-artifact", logText,
            StringComparison.OrdinalIgnoreCase);
        var questionnaireText = questionnaireItems.Values.First()
            .GetProperty("text").GetString();
        Assert.False(string.IsNullOrWhiteSpace(questionnaireText));
        Assert.DoesNotContain(questionnaireText!, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(authentication.AccessToken, logText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CHEST_PAIN")]
    [InlineData("OTHER_SYMPTOMS")]
    public async Task ExpandedPathway_UsesTheGenericValidNeutralFhirPipeline(string pathway)
    {
        await EnsureMigratedAsync();
        var store = new MutableArtifactStore();
        using var factory = Factory(store);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(
            factory,
            client,
            $"fhir-{pathway.ToLowerInvariant()}");
        SetBearer(client, authentication.AccessToken);
        var source = await CompletePreTriageAsync(
            client,
            authentication.Account.ProfileId,
            pathway);

        using var create = await client.PostAsJsonAsync(
            CreateEndpoint(authentication.Account.ProfileId),
            Request(source.EventId, Guid.NewGuid()));
        var metadata = await create.Content.ReadFromJsonAsync<ExportMetadata>();
        using var download = await client.GetAsync(ContentEndpoint(metadata!.Id));
        var bytes = await download.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal("Validated", metadata.Status);
        Assert.Equal("Passed", metadata.Validation!.Outcome);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        using var document = JsonDocument.Parse(bytes);
        var resources = document.RootElement.GetProperty("entry")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("resource"))
            .ToArray();
        Assert.Equal(
            ["QuestionnaireResponse", "Device", "Provenance"],
            resources.Select(resource => resource.GetProperty("resourceType").GetString()));
        Assert.Equal(
            ["DURATION", "INTENSITY", "ADDITIONAL_SYMPTOMS"],
            resources[0].GetProperty("item").EnumerateArray()
                .Select(item => item.GetProperty("linkId").GetString()));
        Assert.DoesNotContain("RiskAssessment", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task AuthenticationAuthorizationAndRevocationAreEnforcedForEveryOperation()
    {
        await EnsureMigratedAsync();
        var store = new MutableArtifactStore();
        using var factory = Factory(store);
        using var ownerClient = factory.CreateApiClient();
        using var managerClient = factory.CreateApiClient();
        using var unrelatedClient = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, ownerClient, "fhir-auth-owner");
        var manager = await AuthenticateAsync(factory, managerClient, "fhir-auth-manager");
        var unrelated = await AuthenticateAsync(factory, unrelatedClient, "fhir-auth-other");
        SetBearer(ownerClient, owner.AccessToken);
        SetBearer(managerClient, manager.AccessToken);
        SetBearer(unrelatedClient, unrelated.AccessToken);
        var source = await CompletePreTriageAsync(ownerClient, owner.Account.ProfileId);
        var now = DateTimeOffset.UtcNow;
        var relationship = CareRelationship.Create(
            EntityId.From(manager.Account.ProfileId),
            EntityId.From(owner.Account.ProfileId),
            CareRelationshipType.Caregiver,
            EntityId.From(manager.Account.AccountId),
            AuthorizationAttestation.Create("phase-6.7-api-test", now),
            now);
        await using (var seed = CreateDbContext())
        {
            seed.CareRelationships.Add(relationship);
            await seed.SaveChangesAsync();
        }

        using var managerCreate = await managerClient.PostAsJsonAsync(
            CreateEndpoint(owner.Account.ProfileId),
            Request(source.EventId, Guid.NewGuid()));
        var export = await managerCreate.Content.ReadFromJsonAsync<ExportMetadata>();
        Assert.Equal(HttpStatusCode.Created, managerCreate.StatusCode);
        Assert.Equal("Validated", export!.Status);

        using var managerMetadata = await managerClient.GetAsync(MetadataEndpoint(export.Id));
        using var managerContent = await managerClient.GetAsync(ContentEndpoint(export.Id));
        using var ownerMetadata = await ownerClient.GetAsync(MetadataEndpoint(export.Id));
        using var ownerContent = await ownerClient.GetAsync(ContentEndpoint(export.Id));
        Assert.Equal(HttpStatusCode.OK, managerMetadata.StatusCode);
        Assert.Equal(HttpStatusCode.OK, managerContent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownerMetadata.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownerContent.StatusCode);

        using var unrelatedCreate = await unrelatedClient.PostAsJsonAsync(
            CreateEndpoint(owner.Account.ProfileId),
            Request(source.EventId, Guid.NewGuid()));
        using var unrelatedMetadata = await unrelatedClient.GetAsync(
            MetadataEndpoint(export.Id));
        using var unrelatedContent = await unrelatedClient.GetAsync(
            ContentEndpoint(export.Id));
        using var absent = await unrelatedClient.GetAsync(MetadataEndpoint(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, unrelatedCreate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unrelatedMetadata.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unrelatedContent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(
            await absent.Content.ReadFromJsonAsync<ProblemResponse>(),
            await unrelatedMetadata.Content.ReadFromJsonAsync<ProblemResponse>());

        await using (var revoke = CreateDbContext())
        {
            var persisted = await revoke.CareRelationships.SingleAsync(
                candidate => candidate.Id == relationship.Id);
            persisted.Revoke(
                EntityId.From(manager.Account.AccountId),
                now.AddSeconds(1));
            await revoke.SaveChangesAsync();
        }

        using var revokedMetadata = await managerClient.GetAsync(MetadataEndpoint(export.Id));
        using var revokedContent = await managerClient.GetAsync(ContentEndpoint(export.Id));
        using var revokedCreate = await managerClient.PostAsJsonAsync(
            CreateEndpoint(owner.Account.ProfileId),
            Request(source.EventId, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, revokedMetadata.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revokedContent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revokedCreate.StatusCode);

        ownerClient.DefaultRequestHeaders.Authorization = null;
        using var anonymousCreate = await ownerClient.PostAsJsonAsync(
            CreateEndpoint(owner.Account.ProfileId),
            Request(source.EventId, Guid.NewGuid()));
        using var anonymousContent = await ownerClient.GetAsync(ContentEndpoint(export.Id));
        SetBearer(ownerClient, "invalid-token");
        using var invalid = await ownerClient.GetAsync(MetadataEndpoint(export.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousContent.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [Fact]
    public async Task IdempotencyIsDatabaseBackedForSequentialConcurrentAndPatientScopes()
    {
        await EnsureMigratedAsync();
        var store = new MutableArtifactStore();
        using var factory = Factory(store);
        using var firstClient = factory.CreateApiClient();
        using var secondClient = factory.CreateApiClient();
        var first = await AuthenticateAsync(factory, firstClient, "fhir-idem-a");
        var second = await AuthenticateAsync(factory, secondClient, "fhir-idem-b");
        SetBearer(firstClient, first.AccessToken);
        SetBearer(secondClient, second.AccessToken);
        var firstSource = await CompletePreTriageAsync(firstClient, first.Account.ProfileId);
        var secondSource = await CompletePreTriageAsync(secondClient, second.Account.ProfileId);
        var sharedKey = Guid.NewGuid();

        var concurrent = await Task.WhenAll(
            firstClient.PostAsJsonAsync(
                CreateEndpoint(first.Account.ProfileId),
                Request(firstSource.EventId, sharedKey)),
            firstClient.PostAsJsonAsync(
                CreateEndpoint(first.Account.ProfileId),
                Request(firstSource.EventId, sharedKey)));
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Created],
            concurrent.Select(value => value.StatusCode).Order().ToArray());
        var concurrentMetadata = await Task.WhenAll(concurrent.Select(response =>
            response.Content.ReadFromJsonAsync<ExportMetadata>()));
        Assert.Single(concurrentMetadata.Select(value => value!.Id).Distinct());

        using var sequential = await firstClient.PostAsJsonAsync(
            CreateEndpoint(first.Account.ProfileId),
            Request(firstSource.EventId, sharedKey));
        var sequentialMetadata = await sequential.Content.ReadFromJsonAsync<ExportMetadata>();
        Assert.Equal(HttpStatusCode.OK, sequential.StatusCode);
        Assert.Equal(concurrentMetadata[0]!.Id, sequentialMetadata!.Id);

        var conflictingSource = await CompletePreTriageAsync(
            firstClient,
            first.Account.ProfileId);
        using var conflictingReplay = await firstClient.PostAsJsonAsync(
            CreateEndpoint(first.Account.ProfileId),
            Request(conflictingSource.EventId, sharedKey));
        Assert.Equal(HttpStatusCode.Conflict, conflictingReplay.StatusCode);

        using var differentKey = await firstClient.PostAsJsonAsync(
            CreateEndpoint(first.Account.ProfileId),
            Request(firstSource.EventId, Guid.NewGuid()));
        var differentMetadata = await differentKey.Content.ReadFromJsonAsync<ExportMetadata>();
        Assert.Equal(HttpStatusCode.Created, differentKey.StatusCode);
        Assert.NotEqual(sequentialMetadata.Id, differentMetadata!.Id);

        using var otherPatientSameKey = await secondClient.PostAsJsonAsync(
            CreateEndpoint(second.Account.ProfileId),
            Request(secondSource.EventId, sharedKey));
        var otherMetadata = await otherPatientSameKey.Content
            .ReadFromJsonAsync<ExportMetadata>();
        Assert.Equal(HttpStatusCode.Created, otherPatientSameKey.StatusCode);
        Assert.NotEqual(sequentialMetadata.Id, otherMetadata!.Id);

        await using var verify = CreateDbContext();
        Assert.Equal(2, await verify.FhirExports.CountAsync(candidate =>
            candidate.PatientProfileId == EntityId.From(first.Account.ProfileId)));
        Assert.Single(await verify.FhirExports.Where(candidate =>
            candidate.PatientProfileId == EntityId.From(second.Account.ProfileId))
            .ToListAsync());
        Assert.Equal(3, store.Count);
        foreach (var response in concurrent)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task ValidationFailureIsSafeAndAllNonValidatedStatesRejectContent()
    {
        await EnsureMigratedAsync();
        var store = new MutableArtifactStore();
        using var factory = Factory(store, validator: new AlwaysInvalidValidator());
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "fhir-invalid");
        SetBearer(client, authentication.AccessToken);
        var source = await CompletePreTriageAsync(client, authentication.Account.ProfileId);

        using var create = await client.PostAsJsonAsync(
            CreateEndpoint(authentication.Account.ProfileId),
            Request(source.EventId, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, create.StatusCode);
        var failureBody = await create.Content.ReadAsStringAsync();
        Assert.DoesNotContain("resourceType", failureBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("validator-test-detail", failureBody,
            StringComparison.OrdinalIgnoreCase);

        await using var db = CreateDbContext();
        var failed = await db.FhirExports.AsNoTracking().SingleAsync(candidate =>
            candidate.PatientProfileId == EntityId.From(authentication.Account.ProfileId));
        Assert.Equal(FhirExportStatus.ValidationFailed, failed.Status);
        using var failedMetadata = await client.GetAsync(MetadataEndpoint(failed.Id.Value));
        var metadata = await failedMetadata.Content.ReadFromJsonAsync<ExportMetadata>();
        using var failedContent = await client.GetAsync(ContentEndpoint(failed.Id.Value));
        Assert.Equal(HttpStatusCode.OK, failedMetadata.StatusCode);
        Assert.Equal("ValidationFailed", metadata!.Status);
        Assert.Equal(HttpStatusCode.Conflict, failedContent.StatusCode);

        var historyEvent = await db.ClinicalHistoryEvents.AsNoTracking().SingleAsync(
            candidate => candidate.Id == EntityId.From(source.EventId));
        var pending = FhirExport.CreatePending(
            historyEvent,
            FhirR4BaseMvp.MappingSpecification().ToExportVersionMetadata(),
            EntityId.New(),
            DateTimeOffset.UtcNow);
        var generated = CreateGenerated(
            historyEvent,
            store,
            FhirR4BaseMvp.FhirRelease,
            FhirR4BaseMvp.MappingVersion);
        var legacy = CreateGenerated(
            historyEvent,
            store,
            FhirSnapshotArtifactFormat.UnresolvedFhirReleaseMarker,
            "legacy-release-neutral");
        db.FhirExports.AddRange(pending, generated, legacy);
        await db.SaveChangesAsync();

        foreach (var export in new[] { pending, generated, legacy })
        {
            using var content = await client.GetAsync(ContentEndpoint(export.Id.Value));
            Assert.Equal(HttpStatusCode.Conflict, content.StatusCode);
        }
    }

    [Fact]
    public async Task IntegrityFailureAndMalformedRequestsNeverExposePrivateContent()
    {
        await EnsureMigratedAsync();
        var store = new MutableArtifactStore();
        using var logs = new InMemoryLoggerProvider();
        using var factory = Factory(store, logs);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "fhir-integrity");
        SetBearer(client, authentication.AccessToken);
        var source = await CompletePreTriageAsync(client, authentication.Account.ProfileId);
        using var create = await client.PostAsJsonAsync(
            CreateEndpoint(authentication.Account.ProfileId),
            Request(source.EventId, Guid.NewGuid()));
        var export = await create.Content.ReadFromJsonAsync<ExportMetadata>();
        await using (var db = CreateDbContext())
        {
            var persisted = await db.FhirExports.AsNoTracking().SingleAsync(
                candidate => candidate.Id == EntityId.From(export!.Id));
            store.Tamper(persisted.PrivateArtifactStorageUri!,
                "private-tampered-fhir-body"u8.ToArray());
        }

        using var content = await client.GetAsync(ContentEndpoint(export!.Id));
        var problem = await content.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, content.StatusCode);
        Assert.DoesNotContain("private-tampered-fhir-body", problem,
            StringComparison.Ordinal);
        Assert.DoesNotContain("beeexy-private-artifact", problem,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".snapshot", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("download rejected by integrity verification",
            string.Join('\n', logs.Messages));

        using var malformedRoute = await client.PostAsJsonAsync(
            "/api/v1/patients/not-a-guid/fhir-exports",
            Request(source.EventId, Guid.NewGuid()));
        using var malformedJson = await client.PostAsync(
            CreateEndpoint(authentication.Account.ProfileId),
            new StringContent("{", Encoding.UTF8, "application/json"));
        using var unsupported = await client.PostAsJsonAsync(
            CreateEndpoint(authentication.Account.ProfileId),
            new
            {
                sourceClinicalHistoryEventId = source.EventId,
                idempotencyKey = Guid.NewGuid(),
                fhirVersion = "R5"
            });
        using var missingPatient = await client.PostAsJsonAsync(
            CreateEndpoint(Guid.NewGuid()),
            Request(source.EventId, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, malformedRoute.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformedJson.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsupported.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingPatient.StatusCode);
    }

    private BeeexyApiFactory Factory(
        MutableArtifactStore store,
        InMemoryLoggerProvider? logger = null,
        IFhirValidator? validator = null) => new(
            postgres.ConnectionString,
            loggerProvider: logger,
            configureServices: services =>
            {
                services.RemoveAll<IFhirArtifactStore>();
                services.AddSingleton<IFhirArtifactStore>(store);
                if (validator is not null)
                {
                    services.RemoveAll<IFhirValidator>();
                    services.AddSingleton(validator);
                }
            });

    private async Task<AuthenticationResult> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client,
        string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        using var challenge = await client.PostAsJsonAsync(
            "/api/v1/auth/email/challenges",
            new { email });
        Assert.Equal(HttpStatusCode.Accepted, challenge.StatusCode);
        var message = Assert.Single(
            factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>().Messages,
            candidate => candidate.Recipient.Value == email);
        using var verification = await client.PostAsJsonAsync(
            "/api/v1/auth/email/verify",
            new { email, code = message.OneTimeCode });
        verification.EnsureSuccessStatusCode();
        return (await verification.Content.ReadFromJsonAsync<AuthenticationResult>())!;
    }

    private static async Task<SourceResult> CompletePreTriageAsync(
        HttpClient client,
        Guid patientId,
        string pathway = "HEADACHE")
    {
        using var start = await client.PostAsJsonAsync(
            "/api/v1/pre-triage/sessions",
            new { pathway });
        var started = await start.Content.ReadFromJsonAsync<StartedSession>();
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        using var answer = await client.PostAsJsonAsync(
            $"/api/v1/pre-triage/sessions/{started!.SessionId:D}/answers",
            new
            {
                structured = new
                {
                    duration = new { value = 2, unit = "DAYS" },
                    intensity = 7,
                    additionalSymptoms = new[] { "FEVER" }
                }
            });
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        if (pathway != "OTHER_SYMPTOMS")
        {
            using var offer = await client.PostAsJsonAsync(
                $"/api/v1/pre-triage/sessions/{started.SessionId:D}/educational-video-offer",
                new { decision = "SKIP" });
            Assert.Equal(HttpStatusCode.OK, offer.StatusCode);
        }
        using var complete = await client.PostAsync(
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/complete",
            null);
        var completed = await complete.Content.ReadFromJsonAsync<CompletedSession>();
        Assert.Equal(HttpStatusCode.Created, complete.StatusCode);
        using var history = await client.GetAsync(
            $"/api/v1/patients/{patientId:D}/clinical-history");
        var page = await history.Content.ReadFromJsonAsync<HistoryPage>();
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var item = Assert.Single(page!.Items, value => value.Source.Id == completed!.EpisodeId);
        return new SourceResult(item.EventId, completed!.EpisodeId);
    }

    private async Task<string> SourceSnapshotAsync(Guid episodeId)
    {
        await using var db = CreateDbContext();
        var episode = await db.PreTriageEpisodes.AsNoTracking()
            .Include(value => value.Answers)
            .SingleAsync(value => value.Id == EntityId.From(episodeId));
        return string.Join('|',
            episode.Id,
            episode.CompletedAt,
            episode.Answers.Count,
            string.Join(';', episode.Answers.OrderBy(value => value.Sequence)
                .Select(value => $"{value.Id}:{value.AnswerJson}:{value.RecordedAt:O}")));
    }

    private static FhirExport CreateGenerated(
        Beeexy.Domain.History.ClinicalHistoryEvent source,
        MutableArtifactStore store,
        string fhirVersion,
        string mappingVersion)
    {
        var bytes = Encoding.UTF8.GetBytes("historical private artifact");
        var reference = FhirArtifactStorageReference.CreateNew();
        store.StoreImmutableAsync(reference, bytes).GetAwaiter().GetResult();
        var createdAt = DateTimeOffset.UtcNow;
        var export = FhirExport.CreatePending(
            source,
            FhirExportVersionMetadata.Create(fhirVersion, mappingVersion),
            EntityId.New(),
            createdAt);
        export.MarkGenerated(
            FhirArtifactMetadata.Create(
                FhirArtifactChecksumCalculator.Algorithm,
                new FhirArtifactChecksumCalculator().Calculate(bytes),
                reference.PrivateUri),
            createdAt);
        return export;
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task EnsureMigratedAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    private static object Request(Guid sourceId, Guid key) => new
    {
        sourceClinicalHistoryEventId = sourceId,
        idempotencyKey = key
    };

    private static string CreateEndpoint(Guid patientId) =>
        $"/api/v1/patients/{patientId:D}/fhir-exports";

    private static string MetadataEndpoint(Guid exportId) =>
        $"/api/v1/fhir-exports/{exportId:D}";

    private static string ContentEndpoint(Guid exportId) =>
        $"/api/v1/fhir-exports/{exportId:D}/content";

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

    private sealed class MutableArtifactStore : IFhirArtifactStore
    {
        private readonly ConcurrentDictionary<string, byte[]> artifacts =
            new(StringComparer.Ordinal);

        public int Count => artifacts.Count;

        public Task StoreImmutableAsync(FhirArtifactStorageReference reference,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default)
        {
            if (!artifacts.TryAdd(reference.PrivateUri, artifactBytes.ToArray()))
            {
                throw new FhirArtifactAlreadyExistsException();
            }

            return Task.CompletedTask;
        }

        public Task<byte[]> ReadAsync(FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Get(reference.PrivateUri));

        public Task<bool> DeleteAsync(FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(artifacts.TryRemove(reference.PrivateUri, out _));

        public byte[] Get(string privateUri) => artifacts.TryGetValue(privateUri, out var value)
            ? value.ToArray()
            : throw new FileNotFoundException();

        public void Tamper(string privateUri, byte[] bytes) => artifacts[privateUri] = bytes;
    }

    private sealed class AlwaysInvalidValidator : IFhirValidator
    {
        public Task<FhirValidatorExecutionResult> ValidateAsync(
            FhirValidatorRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FhirValidatorExecutionResult.Invalid(
                FhirValidatorMetadata.Create("controlled-invalid-validator", "6.7-test"),
                [new FhirValidatorDiagnostic(
                    FhirValidationDiagnosticSeverity.Error,
                    "controlled-invalid",
                    "validator-test-detail")]));
    }

    private sealed record AuthenticationResult(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);

    private sealed record StartedSession(Guid SessionId);

    private sealed record CompletedSession(Guid EpisodeId, DateTimeOffset CompletedAt);

    private sealed record HistoryPage(IReadOnlyList<HistoryItem> Items, string? NextCursor);

    private sealed record HistoryItem(Guid EventId, HistorySource Source);

    private sealed record HistorySource(Guid Id);

    private sealed record SourceResult(Guid EventId, Guid EpisodeId);

    private sealed record ExportMetadata(
        Guid Id,
        string Status,
        string FhirVersion,
        string MappingVersion,
        DateTimeOffset CreatedAt,
        DateTimeOffset? GeneratedAt,
        DateTimeOffset? ValidationCompletedAt,
        ValidationMetadata? Validation);

    private sealed record ValidationMetadata(
        string Outcome,
        int ErrorCount,
        int WarningCount,
        DateTimeOffset CompletedAt);

    private sealed record ProblemResponse(
        int Status,
        string Title,
        string? Type,
        string? Detail,
        string? ErrorCode);
}
