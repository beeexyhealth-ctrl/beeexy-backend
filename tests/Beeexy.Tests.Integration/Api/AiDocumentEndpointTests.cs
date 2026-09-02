using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Ai;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase105")]
[Trait("Category", "Phase108")]
public sealed class AiDocumentEndpointTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task ValidTxtAndTextNativePdf_ArePrivateAndPersistSafeMetadata()
    {
        await EnsureMigratedAsync();
        var blobs = new MemoryBlobStore();
        using var factory = Factory(blobs);
        using var client = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, client, "document-valid");
        SetBearer(client, owner.AccessToken);

        using var txt = await UploadAsync(client, "notes.txt", "text/plain", "Useful notes"u8.ToArray());
        using var pdf = await UploadAsync(client, "notes.pdf", "application/pdf", CreatePdf("Embedded health notes"));
        var txtBody = await txt.Content.ReadFromJsonAsync<DocumentResponse>();
        var pdfBody = await pdf.Content.ReadFromJsonAsync<DocumentResponse>();

        Assert.Equal(HttpStatusCode.Created, txt.StatusCode);
        Assert.Equal(HttpStatusCode.Created, pdf.StatusCode);
        Assert.Equal("text/plain", txtBody!.ContentType);
        Assert.Equal("application/pdf", pdfBody!.ContentType);
        Assert.Equal("active", txtBody.Status);
        Assert.True(txtBody.ExpiresAt <= txtBody.UploadedAt.AddHours(24));
        var responseJson = await txt.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storage", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blob", responseJson, StringComparison.OrdinalIgnoreCase);

        await using var dbContext = CreateDbContext();
        var documents = await dbContext.AiUploadedDocuments.AsNoTracking()
            .Where(document => document.AccountId == EntityId.From(owner.Account.AccountId))
            .OrderBy(document => document.ContentType)
            .ToArrayAsync();
        Assert.Equal(2, documents.Length);
        Assert.All(documents, document =>
        {
            Assert.Equal(AiDocumentStatus.Active, document.Status);
            Assert.Equal(document.CreatedAt.AddHours(24), document.ExpiresAt);
            Assert.True(blobs.Content.ContainsKey(document.StorageKey));
            Assert.Matches("^[0-9a-f]{64}$", document.StorageKey);
        });
        Assert.False(await dbContext.AiAnalysisRequests.AsNoTracking().AnyAsync(
            request => request.AccountId == EntityId.From(owner.Account.AccountId)));
        Assert.False(await dbContext.ClinicalHistoryEvents.AsNoTracking().AnyAsync(
            history => history.PatientProfileId == EntityId.From(owner.Account.ProfileId)));
        Assert.False(await dbContext.FhirExports.AsNoTracking().AnyAsync(
            export => export.PatientProfileId == EntityId.From(owner.Account.ProfileId)));
    }

    [Theory]
    [InlineData("notes.docx", "application/octet-stream", "hello", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("notes.pdf", "application/pdf", "plain text", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("notes.txt", "text/plain", "", HttpStatusCode.UnprocessableEntity)]
    [InlineData("notes.txt", "text/plain", "   ", HttpStatusCode.UnprocessableEntity)]
    [InlineData("notes.pdf", "application/pdf", "%PDF-1.7 garbage", HttpStatusCode.UnprocessableEntity)]
    public async Task InvalidDocuments_ReturnApprovedSafeStatus(
        string fileName,
        string contentType,
        string value,
        HttpStatusCode expected)
    {
        await EnsureMigratedAsync();
        var blobs = new MemoryBlobStore();
        using var factory = Factory(blobs);
        using var client = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, client, "document-invalid");
        SetBearer(client, owner.AccessToken);

        using var response = await UploadAsync(
            client, fileName, contentType, Encoding.ASCII.GetBytes(value));

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(blobs.Content);
        var body = await response.Content.ReadAsStringAsync();
        if (value.Length > 0)
        {
            Assert.DoesNotContain(value, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ScannedPdfMalwareBinaryTxtAndOversize_AreRejectedWithoutStorage()
    {
        await EnsureMigratedAsync();
        var blobs = new MemoryBlobStore();
        using var factory = Factory(blobs);
        using var client = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, client, "document-security");
        SetBearer(client, owner.AccessToken);

        var emptyPdf = new PdfDocumentBuilder();
        emptyPdf.AddPage(PageSize.A4);
        using var scanned = await UploadAsync(client, "scan.pdf", "application/pdf", emptyPdf.Build());
        using var binary = await UploadAsync(client, "binary.txt", "text/plain", [0, 1, 2, 0xff]);
        using var malware = await UploadAsync(
            client,
            "malware.txt",
            "text/plain",
            Encoding.ASCII.GetBytes(
                "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!"));
        using var oversized = await UploadAsync(
            client,
            "large.txt",
            "text/plain",
            Enumerable.Repeat((byte)'a', (int)AiDocumentOptions.MaximumAllowedBytes + 1).ToArray());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, scanned.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, binary.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, malware.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Empty(blobs.Content);
    }

    [Fact]
    public async Task Delete_IsOwnerOnlyPhysicalRetainedAndIdempotent()
    {
        await EnsureMigratedAsync();
        var blobs = new MemoryBlobStore();
        using var factory = Factory(blobs);
        using var ownerClient = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, ownerClient, "document-owner");
        SetBearer(ownerClient, owner.AccessToken);
        using var foreignClient = factory.CreateApiClient();
        var foreign = await AuthenticateAsync(factory, foreignClient, "document-foreign");
        SetBearer(foreignClient, foreign.AccessToken);
        using var upload = await UploadAsync(ownerClient, "notes.txt", "text/plain", "Useful text"u8.ToArray());
        var created = await upload.Content.ReadFromJsonAsync<DocumentResponse>();

        using var concealed = await foreignClient.DeleteAsync(Endpoint(created!.DocumentId));
        using var deleted = await ownerClient.DeleteAsync(Endpoint(created.DocumentId));
        using var repeated = await ownerClient.DeleteAsync(Endpoint(created.DocumentId));
        using var missing = await ownerClient.DeleteAsync(Endpoint(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, concealed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty(blobs.Content);
        await using var dbContext = CreateDbContext();
        var document = await dbContext.AiUploadedDocuments.AsNoTracking().SingleAsync(
            item => item.Id == EntityId.From(created.DocumentId));
        Assert.Equal(AiDocumentStatus.Deleted, document.Status);
        Assert.NotNull(document.DeletedAt);
        Assert.Equal(EntityId.From(owner.Account.AccountId), document.AccountId);
    }

    [Fact]
    public async Task BothEndpointsRequireBearer()
    {
        await EnsureMigratedAsync();
        using var factory = Factory(new MemoryBlobStore());
        using var client = factory.CreateApiClient();
        using var upload = await UploadAsync(client, "notes.txt", "text/plain", "Useful text"u8.ToArray());
        using var delete = await client.DeleteAsync(Endpoint(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, upload.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    [Fact]
    public async Task PostgreSqlExpiryQuery_RemovesDueBlobAndRetainsLifecycleMetadata()
    {
        await EnsureMigratedAsync();
        var blobs = new MemoryBlobStore();
        using var factory = Factory(blobs);
        using var client = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, client, "document-expiry");
        // Use an isolated historical cutoff because the shared migration fixture retains
        // independently seeded Phase 10 foundation rows across test cases.
        var now = new DateTimeOffset(2000, 1, 2, 12, 0, 0, TimeSpan.Zero);
        var due = AiUploadedDocument.Create(
            EntityId.From(owner.Account.AccountId),
            AiBlobKey.CreateNew().Value,
            "text/plain",
            5,
            now.AddHours(-24),
            now);
        var future = AiUploadedDocument.Create(
            EntityId.From(owner.Account.AccountId),
            AiBlobKey.CreateNew().Value,
            "text/plain",
            5,
            now,
            now.AddHours(24));
        blobs.Content[due.StorageKey] = "hello"u8.ToArray();
        blobs.Content[future.StorageKey] = "hello"u8.ToArray();
        await using (var seed = CreateDbContext())
        {
            seed.AiUploadedDocuments.AddRange(due, future);
            await seed.SaveChangesAsync();
        }

        await using (var cleanupContext = CreateDbContext())
        {
            var repository = new AiDocumentRepository(cleanupContext);
            var useCase = new ExpireAiDocuments(
                new FixedClock(now),
                new AiDocumentOptions(AiDocumentOptions.MaximumAllowedBytes, TimeSpan.FromMinutes(1), 100),
                blobs,
                repository);
            Assert.Equal(1, await useCase.ExecuteAsync());
            Assert.Equal(future.ExpiresAt, await repository.GetNextExpiryAsync());
        }

        await using var verify = CreateDbContext();
        Assert.Equal(AiDocumentStatus.Expired,
            (await verify.AiUploadedDocuments.AsNoTracking().SingleAsync(item => item.Id == due.Id)).Status);
        Assert.Equal(AiDocumentStatus.Active,
            (await verify.AiUploadedDocuments.AsNoTracking().SingleAsync(item => item.Id == future.Id)).Status);
        Assert.False(blobs.Content.ContainsKey(due.StorageKey));
        Assert.True(blobs.Content.ContainsKey(future.StorageKey));
    }

    [Fact]
    [Trait("Category", "Phase108")]
    public async Task PostgreSqlExpiryPaging_DoesNotLetFailedOldBlobStarveLaterDocuments()
    {
        await EnsureMigratedAsync();
        var blobs = new MemoryBlobStore();
        using var factory = Factory(blobs);
        using var client = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, client, "document-expiry-recovery");
        var now = new DateTimeOffset(2000, 1, 2, 14, 0, 0, TimeSpan.Zero);
        var documents = Enumerable.Range(0, 5)
            .Select(index => AiUploadedDocument.Create(
                EntityId.From(owner.Account.AccountId),
                AiBlobKey.CreateNew().Value,
                "text/plain",
                5,
                now.AddHours(-30).AddMinutes(index),
                now.AddHours(-6).AddMinutes(index)))
            .ToArray();
        var documentIds = documents.Select(document => document.Id).ToArray();
        foreach (var document in documents)
        {
            blobs.Content[document.StorageKey] = "hello"u8.ToArray();
        }

        blobs.FailingKeys.Add(documents[0].StorageKey);
        await using (var seed = CreateDbContext())
        {
            seed.AiUploadedDocuments.AddRange(documents);
            await seed.SaveChangesAsync();
        }

        await using (var firstContext = CreateDbContext())
        {
            var cleanup = new ExpireAiDocuments(
                new FixedClock(now),
                new AiDocumentOptions(
                    AiDocumentOptions.MaximumAllowedBytes,
                    TimeSpan.FromMinutes(1),
                    2),
                blobs,
                new AiDocumentRepository(firstContext));
            await Assert.ThrowsAsync<AggregateException>(() => cleanup.ExecuteAsync());
        }

        await using (var verifyProgress = CreateDbContext())
        {
            var states = await verifyProgress.AiUploadedDocuments.AsNoTracking()
                .Where(document => documentIds.Contains(document.Id))
                .ToDictionaryAsync(document => document.Id, document => document.Status);
            Assert.Equal(AiDocumentStatus.Active, states[documents[0].Id]);
            Assert.All(documents[1..], document =>
                Assert.Equal(AiDocumentStatus.Expired, states[document.Id]));
        }

        blobs.FailingKeys.Clear();
        await using (var retryContext = CreateDbContext())
        {
            var cleanup = new ExpireAiDocuments(
                new FixedClock(now),
                new AiDocumentOptions(
                    AiDocumentOptions.MaximumAllowedBytes,
                    TimeSpan.FromMinutes(1),
                    2),
                blobs,
                new AiDocumentRepository(retryContext));
            Assert.Equal(1, await cleanup.ExecuteAsync());
        }

        await using var verifyRecovery = CreateDbContext();
        Assert.All(await verifyRecovery.AiUploadedDocuments.AsNoTracking()
            .Where(document => documentIds.Contains(document.Id))
            .ToArrayAsync(),
            document => Assert.Equal(AiDocumentStatus.Expired, document.Status));
    }

    private BeeexyApiFactory Factory(MemoryBlobStore blobs) => new(
        postgres.ConnectionString,
        configureServices: services =>
        {
            services.RemoveAll<IAiDocumentBlobStore>();
            services.AddSingleton<IAiDocumentBlobStore>(blobs);
        });

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        string fileName,
        string contentType,
        byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return await client.PostAsync("/api/v1/ai/documents", form);
    }

    private async Task<AuthenticationResult> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client,
        string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        using var challenge = await client.PostAsJsonAsync(
            "/api/v1/auth/email/challenges", new { email });
        Assert.Equal(HttpStatusCode.Accepted, challenge.StatusCode);
        var message = Assert.Single(
            factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>().Messages,
            candidate => candidate.Recipient.Value == email);
        using var verification = await client.PostAsJsonAsync(
            "/api/v1/auth/email/verify", new { email, code = message.OneTimeCode });
        verification.EnsureSuccessStatusCode();
        return (await verification.Content.ReadFromJsonAsync<AuthenticationResult>())!;
    }

    private static byte[] CreatePdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(40, 700), font);
        return builder.Build();
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>().UseNpgsql(postgres.ConnectionString).Options);

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private static string Endpoint(Guid id) => $"/api/v1/ai/documents/{id:D}";
    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private sealed class MemoryBlobStore : IAiDocumentBlobStore
    {
        public Dictionary<string, byte[]> Content { get; } = [];
        public HashSet<string> FailingKeys { get; } = [];
        public Task WritePrivateAsync(AiBlobKey key, ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            Content.Add(key.Value, content.ToArray());
            return Task.CompletedTask;
        }
        public Task<byte[]> ReadPrivateAsync(AiBlobKey key,
            CancellationToken cancellationToken = default) => Task.FromResult(Content[key.Value]);
        public Task<bool> DeleteAsync(AiBlobKey key,
            CancellationToken cancellationToken = default)
        {
            if (FailingKeys.Contains(key.Value))
            {
                throw new IOException("private-blob-path-marker");
            }

            return Task.FromResult(Content.Remove(key.Value));
        }
        public Task<int> DeleteCreatedBeforeAsync(DateTimeOffset cutoff,
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed record AuthenticationResult(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account);
    private sealed record AuthenticationAccount(Guid AccountId, Guid ProfileId, string BeeexyId);
    private sealed record DocumentResponse(
        Guid DocumentId,
        string ContentType,
        long SizeBytes,
        DateTimeOffset UploadedAt,
        DateTimeOffset ExpiresAt,
        string Status);
}
