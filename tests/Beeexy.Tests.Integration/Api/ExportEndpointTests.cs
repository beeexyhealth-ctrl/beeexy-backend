using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Identity;
using Beeexy.Application.Interoperability;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Sharing;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class ExportEndpointTests(PostgreSqlContainerFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = Normalize(DateTimeOffset.UtcNow);
    private readonly string storageRoot = Path.Combine(
        Path.GetTempPath(),
        "beeexy-phase116-integration",
        Guid.NewGuid().ToString("N"));

    [Fact]
    [Trait("Category", "Phase116")]
    public async Task PrimaryCreationReplayAndSourceChange_PreserveImmutableStoredBytes()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "immutable");
        SetBearer(client, authentication.AccessToken);
        var key = Guid.NewGuid();
        var endpoint = Endpoint(authentication.Account.ProfileId);

        using var created = await client.PostAsJsonAsync(endpoint, Request("BeeexyJson", key));
        var createdText = await created.Content.ReadAsStringAsync();
        using var createdJson = JsonDocument.Parse(createdText);
        var metadata = createdJson.RootElement;

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("no-store", created.Headers.CacheControl?.ToString());
        Assert.Equal("BeeexyJson", metadata.GetProperty("format").GetString());
        Assert.Equal(BeeexyJsonExportRenderer.MediaType,
            metadata.GetProperty("mediaType").GetString());
        Assert.Equal("SHA-256", metadata.GetProperty("checksumAlgorithm").GetString());
        Assert.Equal("Available", metadata.GetProperty("status").GetString());
        Assert.Equal(Now, metadata.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(Now.AddDays(30),
            metadata.GetProperty("retentionEligibleAt").GetDateTimeOffset());
        Assert.DoesNotContain("storage", createdText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accountId", createdText, StringComparison.OrdinalIgnoreCase);

        ExportArtifact artifact;
        await using (var dbContext = CreateDbContext())
        {
            artifact = await dbContext.ExportArtifacts.AsNoTracking().SingleAsync(value =>
                value.IdempotencyKey == EntityId.From(key));
            Assert.Equal(EntityId.From(authentication.Account.ProfileId),
                artifact.PatientProfileId);
            Assert.Equal(EntityId.From(authentication.Account.AccountId),
                artifact.RequestedByAccountId);
            Assert.Equal(ExportArtifactStatus.Available, artifact.Status);
        }

        Assert.Equal(artifact.Checksum, metadata.GetProperty("checksum").GetString());

        var store = new FileSystemPrivateArtifactStorage(storageRoot);
        var reference = store.ParseIdentityForVerification(artifact.PrivateStorageIdentity!);
        var originalBytes = await store.ReadForVerificationAsync(reference);
        Assert.Equal(
            new ExportArtifactChecksumCalculator().Calculate(originalBytes),
            artifact.Checksum);
        using (var exported = JsonDocument.Parse(originalBytes))
        {
            Assert.Equal("BeeexyJson", exported.RootElement.GetProperty("format").GetString());
            Assert.Equal("1.0", exported.RootElement.GetProperty("formatVersion").GetString());
            Assert.Equal(authentication.Account.BeeexyId,
                exported.RootElement.GetProperty("profile").GetProperty("demographics")
                    .GetProperty("beeexyId").GetString());
            Assert.False(exported.RootElement.TryGetProperty("privateStorageIdentity", out _));
        }

        using var patch = await client.PatchAsJsonAsync(
            $"/api/v1/patients/{authentication.Account.ProfileId:D}",
            new { firstName = "Changed", version = 1 });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(originalBytes, await store.ReadForVerificationAsync(reference));

        using var replay = await client.PostAsJsonAsync(endpoint, Request("BeeexyJson", key));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(metadata.GetProperty("exportArtifactId").GetGuid(),
            replayJson.RootElement.GetProperty("exportArtifactId").GetGuid());
        Assert.Equal(artifact.Checksum, replayJson.RootElement.GetProperty("checksum").GetString());

        using var newer = await client.PostAsJsonAsync(
            endpoint,
            Request("BeeexyJson", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, newer.StatusCode);
        await using var verify = CreateDbContext();
        Assert.Equal(2, await verify.ExportArtifacts.CountAsync(value =>
            value.PatientProfileId == EntityId.From(authentication.Account.ProfileId)));
        var old = await verify.ExportArtifacts.AsNoTracking().SingleAsync(value =>
            value.Id == artifact.Id);
        Assert.Equal(artifact.Checksum, old.Checksum);
        Assert.Equal(artifact.PrivateStorageIdentity, old.PrivateStorageIdentity);
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task AllFormats_CreateAndBearerDownloadExactPrivateBytes()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "formats");
        SetBearer(client, authentication.AccessToken);
        await CompletePreTriageAsync(client, authentication.Account.ProfileId);

        var artifacts = new Dictionary<string, ExportArtifact>();
        foreach (var format in new[] { "BeeexyJson", "Pdf", "FhirJson" })
        {
            using var creation = await client.PostAsJsonAsync(
                Endpoint(authentication.Account.ProfileId),
                Request(format, Guid.NewGuid()));
            var body = await creation.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
            using var json = JsonDocument.Parse(body);
            var id = json.RootElement.GetProperty("exportArtifactId").GetGuid();
            await using var db = CreateDbContext();
            var artifact = await db.ExportArtifacts.AsNoTracking().SingleAsync(value =>
                value.Id == EntityId.From(id));
            Assert.Equal(format, json.RootElement.GetProperty("format").GetString());
            Assert.Equal(artifact.Checksum, json.RootElement.GetProperty("checksum").GetString());
            artifacts.Add(format, artifact);
        }

        var privateStore = new FileSystemPrivateArtifactStorage(storageRoot);
        foreach (var (format, artifact) in artifacts)
        {
            var stored = await privateStore.ReadAsync(artifact.PrivateStorageIdentity!);
            using var download = await client.GetAsync(
                $"/api/v1/exports/{artifact.Id.Value:D}/content");
            var downloaded = await download.Content.ReadAsByteArrayAsync();

            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("no-store", download.Headers.CacheControl?.ToString());
            Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.False(download.Headers.AcceptRanges.Any());
            Assert.Equal(artifact.MediaType, download.Content.Headers.ContentType?.MediaType);
            Assert.Equal(stored, downloaded);
            Assert.Equal(
                artifact.Checksum,
                new ExportArtifactChecksumCalculator().Calculate(downloaded));
            Assert.DoesNotContain(storageRoot,
                string.Join("|", download.Headers.Select(value => value.ToString())),
                StringComparison.OrdinalIgnoreCase);

            if (format == "Pdf")
            {
                using var pdf = PdfDocument.Open(downloaded);
                var text = string.Join('\n', pdf.GetPages().Select(page =>
                    ContentOrderTextExtractor.GetText(page)));
                Assert.Contains("Beeexy Health Snapshot", text, StringComparison.Ordinal);
                Assert.Contains("Clinical History", text, StringComparison.Ordinal);
                Assert.Contains("Completed Pre-Triage", text, StringComparison.Ordinal);
                Assert.Contains("About this export", text, StringComparison.Ordinal);
                Assert.DoesNotContain("conversation", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("provider", text, StringComparison.OrdinalIgnoreCase);
            }
        }

        var fhirArtifact = artifacts["FhirJson"];
        await using (var db = CreateDbContext())
        {
            var phase6 = await db.FhirExports.AsNoTracking().SingleAsync(value =>
                value.Id == fhirArtifact.SnapshotId);
            var phase6Bytes = await factory.Services.GetRequiredService<IFhirArtifactStore>()
                .ReadAsync(FhirArtifactStorageReference.FromPrivateUri(
                    phase6.PrivateArtifactStorageUri!));
            Assert.Equal(
                phase6Bytes,
                await privateStore.ReadAsync(fhirArtifact.PrivateStorageIdentity!));
        }

        var pdfArtifact = artifacts["Pdf"];
        var before = await privateStore.ReadAsync(pdfArtifact.PrivateStorageIdentity!);
        using var patch = await client.PatchAsJsonAsync(
            $"/api/v1/patients/{authentication.Account.ProfileId:D}",
            new { firstName = "ChangedAfterExport", version = 1 });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        using var replay = await client.PostAsJsonAsync(
            Endpoint(authentication.Account.ProfileId),
            Request("Pdf", pdfArtifact.IdempotencyKey.Value));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(before, await privateStore.ReadAsync(pdfArtifact.PrivateStorageIdentity!));
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task ShareAccess_RequiresExactArtifactItemAndRecordsIdempotentDownloadActivity()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var ownerClient = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, ownerClient, "share-download");
        SetBearer(ownerClient, owner.AccessToken);
        var first = await CreateExportAsync(ownerClient, owner.Account.ProfileId, "BeeexyJson");
        var second = await CreateExportAsync(ownerClient, owner.Account.ProfileId, "Pdf");

        using var share = await ownerClient.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                scope = "SpecificRecords",
                idempotencyKey = Guid.NewGuid(),
                items = new[]
                {
                    new
                    {
                        resourceType = SupportedShareResourceTypes.ExportArtifact,
                        resourceId = first
                    }
                }
            });
        Assert.Equal(HttpStatusCode.Created, share.StatusCode);
        using var shareJson = JsonDocument.Parse(await share.Content.ReadAsStringAsync());
        var grantId = shareJson.RootElement.GetProperty("shareGrantId").GetGuid();
        var capability = shareJson.RootElement.GetProperty("capability").GetString()!;
        var shareToken = await ExchangeAsync(factory, capability);
        using var recipient = factory.CreateApiClient();
        SetBearer(recipient, shareToken);

        using var allowed = await recipient.GetAsync($"/api/v1/exports/{first:D}/content");
        using var repeated = await recipient.GetAsync($"/api/v1/exports/{first:D}/content");
        using var unlisted = await recipient.GetAsync($"/api/v1/exports/{second:D}/content");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unlisted.StatusCode);
        Assert.Contains("export_artifact_forbidden", await unlisted.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var activity = await ownerClient.GetAsync(
            $"/api/v1/shares/{grantId:D}/activity");
        activity.EnsureSuccessStatusCode();
        var activityText = await activity.Content.ReadAsStringAsync();
        using var activityJson = JsonDocument.Parse(activityText);
        var downloaded = activityJson.RootElement.GetProperty("events")
            .EnumerateArray().Where(value =>
                value.GetProperty("eventType").GetString() == "Downloaded").ToArray();
        var downloadEvent = Assert.Single(downloaded);
        Assert.Equal("Succeeded", downloadEvent.GetProperty("outcome").GetString());
        Assert.Equal(SupportedShareResourceTypes.ExportArtifact,
            downloadEvent.GetProperty("resourceCategory").GetString());
        Assert.DoesNotContain("token", activityText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage", activityText, StringComparison.OrdinalIgnoreCase);

        using var fullShare = await ownerClient.PostAsJsonAsync(
            "/api/v1/shares",
            new { scope = "FullProfile", idempotencyKey = Guid.NewGuid() });
        using var fullJson = JsonDocument.Parse(await fullShare.Content.ReadAsStringAsync());
        var fullToken = await ExchangeAsync(
            factory,
            fullJson.RootElement.GetProperty("capability").GetString()!);
        using var fullRecipient = factory.CreateApiClient();
        SetBearer(fullRecipient, fullToken);
        using var fullDenied = await fullRecipient.GetAsync(
            $"/api/v1/exports/{first:D}/content");
        Assert.Equal(HttpStatusCode.Forbidden, fullDenied.StatusCode);

        using var revoke = await ownerClient.PostAsync(
            $"/api/v1/shares/{grantId:D}/revoke",
            null);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using var revoked = await recipient.GetAsync($"/api/v1/exports/{first:D}/content");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task ConcurrentFhirCreation_ConvergesThroughPhase6AndPhase11Idempotency()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "fhir-concurrency");
        SetBearer(client, authentication.AccessToken);
        await CompletePreTriageAsync(client, authentication.Account.ProfileId);
        var key = Guid.NewGuid();
        var endpoint = Endpoint(authentication.Account.ProfileId);

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(endpoint, Request("FhirJson", key)),
            client.PostAsJsonAsync(endpoint, Request("FhirJson", key)));
        try
        {
            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.Created],
                responses.Select(value => value.StatusCode).Order().ToArray());
            var ids = new List<Guid>();
            foreach (var response in responses)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                ids.Add(json.RootElement.GetProperty("exportArtifactId").GetGuid());
            }

            Assert.Single(ids.Distinct());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var db = CreateDbContext();
        Assert.Single(await db.ExportArtifacts.AsNoTracking().Where(value =>
            value.PatientProfileId == EntityId.From(authentication.Account.ProfileId) &&
            value.IdempotencyKey == EntityId.From(key)).ToArrayAsync());
        Assert.Single(await db.FhirExports.AsNoTracking().Where(value =>
            value.PatientProfileId == EntityId.From(authentication.Account.ProfileId) &&
            value.IdempotencyKey == EntityId.From(key)).ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task ShareDownloadAndRevoke_AreOrderedByCurrentGrantLock()
    {
        await EnsureMigratedAsync();
        var storage = new BlockingStorage();
        using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IPrivateArtifactStorage>();
            services.AddSingleton<IPrivateArtifactStorage>(storage);
        });
        using var ownerClient = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, ownerClient, "download-revoke");
        SetBearer(ownerClient, owner.AccessToken);
        var artifactId = await CreateExportAsync(
            ownerClient,
            owner.Account.ProfileId,
            "BeeexyJson");
        using var share = await ownerClient.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                scope = "SpecificRecords",
                idempotencyKey = Guid.NewGuid(),
                items = new[]
                {
                    new
                    {
                        resourceType = SupportedShareResourceTypes.ExportArtifact,
                        resourceId = artifactId
                    }
                }
            });
        using var shareJson = JsonDocument.Parse(await share.Content.ReadAsStringAsync());
        var grantId = shareJson.RootElement.GetProperty("shareGrantId").GetGuid();
        var token = await ExchangeAsync(
            factory,
            shareJson.RootElement.GetProperty("capability").GetString()!);
        using var recipient = factory.CreateApiClient();
        SetBearer(recipient, token);
        storage.BlockReads = true;

        var downloadTask = recipient.GetAsync($"/api/v1/exports/{artifactId:D}/content");
        await storage.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var revokeTask = ownerClient.PostAsync($"/api/v1/shares/{grantId:D}/revoke", null);
        await Task.Delay(200);
        Assert.False(revokeTask.IsCompleted);

        storage.ReleaseRead.TrySetResult();
        using var download = await downloadTask;
        using var revoke = await revokeTask;
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using var denied = await recipient.GetAsync(
            $"/api/v1/exports/{artifactId:D}/content");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    [Trait("Category", "Phase117")]
    public async Task Download_ConcealsForeignArtifactsAndFailsSafelyForIncompleteOrMissingBytes()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var ownerClient = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, ownerClient, "download-state-owner");
        SetBearer(ownerClient, owner.AccessToken);
        var availableId = await CreateExportAsync(
            ownerClient,
            owner.Account.ProfileId,
            "BeeexyJson");

        var pending = ExportArtifact.CreatePending(
            EntityId.From(owner.Account.ProfileId),
            EntityId.From(owner.Account.AccountId),
            EntityId.New(),
            ExportArtifactFormat.Pdf,
            PdfExportContract.MediaType,
            EntityId.New(),
            BeeexyJsonExportRenderer.SnapshotVersion,
            Now,
            Now.AddDays(30));
        var missing = ExportArtifact.CreatePending(
            EntityId.From(owner.Account.ProfileId),
            EntityId.From(owner.Account.AccountId),
            EntityId.New(),
            ExportArtifactFormat.BeeexyJson,
            BeeexyJsonExportRenderer.MediaType,
            EntityId.New(),
            BeeexyJsonExportRenderer.SnapshotVersion,
            Now,
            Now.AddDays(30));
        var missingReference = new FileSystemPrivateArtifactStorage(storageRoot).CreateReference();
        missing.MarkAvailable(
            ExportArtifactContentMetadata.Create(
                ExportArtifactChecksumCalculator.Algorithm,
                new string('a', 64),
                missingReference.PrivateStorageIdentity),
            Now);
        await using (var db = CreateDbContext())
        {
            db.ExportArtifacts.AddRange(pending, missing);
            await db.SaveChangesAsync();
        }

        using var pendingResponse = await ownerClient.GetAsync(
            $"/api/v1/exports/{pending.Id.Value:D}/content");
        using var missingResponse = await ownerClient.GetAsync(
            $"/api/v1/exports/{missing.Id.Value:D}/content");
        Assert.Equal(HttpStatusCode.Conflict, pendingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        using var anonymous = factory.CreateApiClient();
        using var anonymousResponse = await anonymous.GetAsync(
            $"/api/v1/exports/{availableId:D}/content");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var foreignClient = factory.CreateApiClient();
        var foreign = await AuthenticateAsync(factory, foreignClient, "download-state-foreign");
        SetBearer(foreignClient, foreign.AccessToken);
        using var foreignResponse = await foreignClient.GetAsync(
            $"/api/v1/exports/{availableId:D}/content");
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public async Task IdempotencyConcurrencyAndFormatBoundaries_ConvergeWithoutFallback()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "concurrent");
        SetBearer(client, authentication.AccessToken);
        var endpoint = Endpoint(authentication.Account.ProfileId);
        var key = Guid.NewGuid();

        var firstTask = client.PostAsJsonAsync(endpoint, Request("BeeexyJson", key));
        var secondTask = client.PostAsJsonAsync(endpoint, Request("BeeexyJson", key));
        var responses = await Task.WhenAll(firstTask, secondTask);
        try
        {
            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.Created],
                responses.Select(value => value.StatusCode).Order().ToArray());
            var ids = new List<Guid>();
            foreach (var response in responses)
            {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                ids.Add(body.RootElement.GetProperty("exportArtifactId").GetGuid());
            }

            Assert.Single(ids.Distinct());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        using var incompatible = await client.PostAsJsonAsync(endpoint, Request("Pdf", key));
        Assert.Equal(HttpStatusCode.Conflict, incompatible.StatusCode);
        using var pdf = await client.PostAsJsonAsync(endpoint, Request("Pdf", Guid.NewGuid()));
        using var fhir = await client.PostAsJsonAsync(endpoint, Request("FhirJson", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, pdf.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, fhir.StatusCode);
        Assert.Contains("export_generation_unavailable", await fhir.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        await using var dbContext = CreateDbContext();
        var artifacts = await dbContext.ExportArtifacts.AsNoTracking()
            .Where(value => value.PatientProfileId ==
                EntityId.From(authentication.Account.ProfileId))
            .ToArrayAsync();
        Assert.Equal(2, artifacts.Length);
        Assert.All(artifacts, value => Assert.Equal(
            ExportArtifactStatus.Available,
            value.Status));
        Assert.Equal(
            [ExportArtifactFormat.BeeexyJson, ExportArtifactFormat.Pdf],
            artifacts.Select(value => value.Format).Order().ToArray());
        Assert.Equal(2, Directory.EnumerateFiles(storageRoot, "*.artifact").Count());
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public async Task AuthorizationAndRequestShape_EnforcePrimaryOnlyConcealment()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "authorization");
        var endpoint = Endpoint(authentication.Account.ProfileId);

        using var anonymous = await client.PostAsJsonAsync(
            endpoint,
            Request("BeeexyJson", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        SetBearer(client, authentication.AccessToken);
        using var unknown = await client.PostAsJsonAsync(
            Endpoint(Guid.NewGuid()),
            Request("BeeexyJson", Guid.NewGuid()));
        using var beeexyId = await client.PostAsJsonAsync(
            $"/api/v1/patients/{authentication.Account.BeeexyId}/exports",
            Request("BeeexyJson", Guid.NewGuid()));
        using var overrideAttempt = await client.PostAsJsonAsync(
            endpoint,
            new
            {
                format = "BeeexyJson",
                idempotencyKey = Guid.NewGuid(),
                patientId = authentication.Account.ProfileId,
                retentionDays = 1
            });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, beeexyId.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, overrideAttempt.StatusCode);
        using var unknownFormat = await client.PostAsJsonAsync(
            endpoint,
            Request("Unknown", Guid.NewGuid()));
        using var emptyKey = await client.PostAsJsonAsync(
            endpoint,
            Request("BeeexyJson", Guid.Empty));
        using var malformed = await client.PostAsync(
            endpoint,
            new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownFormat.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, emptyKey.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        using var managedCreation = await client.PostAsJsonAsync(
            "/api/v1/care-relationships",
            new
            {
                relationshipType = "Child",
                attestationVersion = "phase-11.6-test",
                attestationAccepted = true,
                patient = new
                {
                    firstName = "Managed",
                    lastName = "Patient",
                    dateOfBirth = "2012-05-12",
                    sexAssignedAtBirth = "Female",
                    state = "NY"
                }
            });
        Assert.Equal(HttpStatusCode.Created, managedCreation.StatusCode);
        using var managedJson = JsonDocument.Parse(
            await managedCreation.Content.ReadAsStringAsync());
        var managedId = managedJson.RootElement.GetProperty("patient")
            .GetProperty("profileId").GetGuid();
        var relationshipId = managedJson.RootElement.GetProperty("relationship")
            .GetProperty("id").GetGuid();
        using var managed = await client.PostAsJsonAsync(
            Endpoint(managedId),
            Request("BeeexyJson", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, managed.StatusCode);
        using var revoke = await client.DeleteAsync($"/api/v1/care-relationships/{relationshipId:D}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using var revokedManaged = await client.PostAsJsonAsync(
            Endpoint(managedId),
            Request("BeeexyJson", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, revokedManaged.StatusCode);

        using var share = await client.PostAsJsonAsync(
            "/api/v1/shares",
            new { scope = "FullProfile", idempotencyKey = Guid.NewGuid() });
        using var shareJson = JsonDocument.Parse(await share.Content.ReadAsStringAsync());
        var capability = shareJson.RootElement.GetProperty("capability").GetString();
        using var exchangeClient = factory.CreateApiClient();
        using var exchange = await exchangeClient.PostAsJsonAsync(
            "/api/v1/shared-access/exchange",
            new { capability });
        using var exchangeJson = JsonDocument.Parse(await exchange.Content.ReadAsStringAsync());
        var shareToken = exchangeJson.RootElement.GetProperty("accessToken").GetString();
        using var shareClient = factory.CreateApiClient();
        SetBearer(shareClient, shareToken!);
        using var shareAttempt = await shareClient.PostAsJsonAsync(
            endpoint,
            Request("BeeexyJson", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, shareAttempt.StatusCode);

        await using (var dbContext = CreateDbContext())
        {
            var account = await dbContext.Accounts.SingleAsync(value =>
                value.Id == EntityId.From(authentication.Account.AccountId));
            account.Disable(Now.AddMinutes(1));
            await dbContext.SaveChangesAsync();
        }

        using var disabled = await client.PostAsJsonAsync(
            endpoint,
            Request("BeeexyJson", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, disabled.StatusCode);
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public async Task StorageFailure_IsSafeAndLeavesNoCompletedMetadata()
    {
        await EnsureMigratedAsync();
        var privatePath = Path.Combine(storageRoot, "must-not-leak");
        using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IPrivateArtifactStorage>();
            services.AddSingleton<IPrivateArtifactStorage>(new FailingStorage(privatePath));
        });
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "storage-failure");
        SetBearer(client, authentication.AccessToken);

        using var response = await client.PostAsJsonAsync(
            Endpoint(authentication.Account.ProfileId),
            Request("BeeexyJson", Guid.NewGuid()));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(privatePath, body, StringComparison.OrdinalIgnoreCase);
        await using var dbContext = CreateDbContext();
        Assert.Empty(await dbContext.ExportArtifacts.AsNoTracking()
            .Where(value => value.PatientProfileId ==
                EntityId.From(authentication.Account.ProfileId))
            .ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "Phase116")]
    [Trait("Category", "Phase117")]
    public async Task OpenApi_CompletesExportCreationAndDualAuthorityDownloadContract()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(60, paths.EnumerateObject().Count());
        var operation = paths.GetProperty("/api/v1/patients/{id}/exports")
            .GetProperty("post");
        var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
        Assert.True(security.TryGetProperty("Bearer", out _));
        Assert.False(security.TryGetProperty("ShareAccess", out _));
        Assert.Equal(
            ["200", "201", "400", "401", "404", "409", "422", "500"],
            operation.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name).Order(StringComparer.Ordinal).ToArray());
        var download = paths.GetProperty("/api/v1/exports/{id}/content")
            .GetProperty("get");
        var downloadSecurity = download.GetProperty("security").EnumerateArray().ToArray();
        Assert.Equal(2, downloadSecurity.Length);
        Assert.Contains(downloadSecurity, value => value.TryGetProperty("Bearer", out _));
        Assert.Contains(downloadSecurity, value => value.TryGetProperty("ShareAccess", out _));
        Assert.Equal(
            ["200", "401", "403", "404", "409", "500"],
            download.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name).Order(StringComparer.Ordinal).ToArray());
        var content = download.GetProperty("responses").GetProperty("200")
            .GetProperty("content");
        Assert.True(content.TryGetProperty(BeeexyJsonExportRenderer.MediaType, out _));
        Assert.True(content.TryGetProperty(PdfExportContract.MediaType, out _));
        Assert.True(content.TryGetProperty("application/fhir+json", out _));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var responseSchema = schemas.GetProperty("ExportArtifactResponse").GetRawText();
        Assert.DoesNotContain("storage", responseSchema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", responseSchema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("capability", responseSchema, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(storageRoot))
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    private BeeexyApiFactory CreateFactory(
        Action<IServiceCollection>? configureServices = null) => new(
        postgres.ConnectionString,
        configurationOverrides: new Dictionary<string, string?>
        {
            ["Exports:RetentionDays"] = "30",
            ["Exports:PrivateStorage:Provider"] = "LocalFileSystem",
            ["Exports:PrivateStorage:LocalRoot"] = storageRoot
        },
        configureServices: services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(new FixedClock(Now));
            configureServices?.Invoke(services);
        });

    private async Task<AuthenticationResult> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client,
        string prefix)
    {
        var email = $"export-{prefix}-{Guid.NewGuid():N}@example.com";
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

    private static async Task CompletePreTriageAsync(HttpClient client, Guid patientId)
    {
        using var start = await client.PostAsJsonAsync(
            "/api/v1/pre-triage/sessions",
            new { pathway = "HEADACHE" });
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
        using var offer = await client.PostAsJsonAsync(
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/educational-video-offer",
            new { decision = "SKIP" });
        Assert.Equal(HttpStatusCode.OK, offer.StatusCode);
        using var complete = await client.PostAsync(
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/complete",
            null);
        Assert.Equal(HttpStatusCode.Created, complete.StatusCode);
        using var history = await client.GetAsync(
            $"/api/v1/patients/{patientId:D}/clinical-history");
        history.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreateExportAsync(
        HttpClient client,
        Guid patientId,
        string format)
    {
        using var response = await client.PostAsJsonAsync(
            Endpoint(patientId),
            Request(format, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("exportArtifactId").GetGuid();
    }

    private static async Task<string> ExchangeAsync(
        BeeexyApiFactory factory,
        string capability)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/shared-access/exchange",
            new { capability });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static string Endpoint(Guid patientId) =>
        $"/api/v1/patients/{patientId:D}/exports";

    private static object Request(string format, Guid key) => new
    {
        format,
        idempotencyKey = key
    };

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static DateTimeOffset Normalize(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(
            utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond),
            TimeSpan.Zero);
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

    private sealed class FailingStorage(string privatePath) : IPrivateArtifactStorage
    {
        private readonly PrivateArtifactStorageReference reference = new(
            new string('b', 64),
            $"beeexy-private-export://local-store/{new string('b', 64)}");

        public PrivateArtifactStorageReference CreateReference() => reference;

        public Task StoreImmutableAsync(
            PrivateArtifactStorageReference reference,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException(privatePath));

        public Task<byte[]> ReadAsync(
            string privateStorageIdentity,
            CancellationToken cancellationToken = default) =>
            Task.FromException<byte[]>(new IOException(privatePath));

        public Task<bool> DeleteAsync(
            PrivateArtifactStorageReference reference,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class BlockingStorage : IPrivateArtifactStorage
    {
        private readonly Dictionary<string, byte[]> artifacts = new(StringComparer.Ordinal);

        public bool BlockReads { get; set; }
        public TaskCompletionSource ReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public PrivateArtifactStorageReference CreateReference()
        {
            var key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            return new PrivateArtifactStorageReference(
                key,
                $"beeexy-private-export://test-store/{key}");
        }

        public Task StoreImmutableAsync(
            PrivateArtifactStorageReference reference,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default)
        {
            artifacts.Add(reference.PrivateStorageIdentity, artifactBytes.ToArray());
            return Task.CompletedTask;
        }

        public async Task<byte[]> ReadAsync(
            string privateStorageIdentity,
            CancellationToken cancellationToken = default)
        {
            if (BlockReads)
            {
                ReadStarted.TrySetResult();
                await ReleaseRead.Task.WaitAsync(cancellationToken);
            }

            return artifacts.TryGetValue(privateStorageIdentity, out var bytes)
                ? bytes
                : throw new FileNotFoundException();
        }

        public Task<bool> DeleteAsync(
            PrivateArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(artifacts.Remove(reference.PrivateStorageIdentity));
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
}
