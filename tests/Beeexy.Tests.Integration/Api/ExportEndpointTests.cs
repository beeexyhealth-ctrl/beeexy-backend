using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Identity;
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
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pdf.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, fhir.StatusCode);
        Assert.Contains("export_format_unavailable", await pdf.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        await using var dbContext = CreateDbContext();
        var artifacts = await dbContext.ExportArtifacts.AsNoTracking()
            .Where(value => value.PatientProfileId ==
                EntityId.From(authentication.Account.ProfileId))
            .ToArrayAsync();
        Assert.Single(artifacts);
        Assert.Equal(ExportArtifactFormat.BeeexyJson, artifacts[0].Format);
        Assert.Equal(ExportArtifactStatus.Available, artifacts[0].Status);
        Assert.Single(Directory.EnumerateFiles(storageRoot, "*.artifact"));
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
    public async Task OpenApi_AddsOnlyBearerExportCreationAndNoDownloadRoute()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(59, paths.EnumerateObject().Count());
        var operation = paths.GetProperty("/api/v1/patients/{id}/exports")
            .GetProperty("post");
        var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
        Assert.True(security.TryGetProperty("Bearer", out _));
        Assert.False(security.TryGetProperty("ShareAccess", out _));
        Assert.Equal(
            ["200", "201", "400", "401", "404", "409", "422", "500"],
            operation.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.False(paths.TryGetProperty("/api/v1/exports/{id}/content", out _));
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

        public Task<bool> DeleteAsync(
            PrivateArtifactStorageReference reference,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed record AuthenticationResult(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);
}
