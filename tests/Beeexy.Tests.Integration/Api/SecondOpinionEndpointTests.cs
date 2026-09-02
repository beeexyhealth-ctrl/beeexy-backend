using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Phase106")]
public sealed class SecondOpinionEndpointTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task TextOnlyRequest_Returns202AndSafeImmutableResultWithMetadata()
    {
        await EnsureMigratedAsync();
        var provider = Provider.Approved("Possible causes could include dehydration.");
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, client, "second-opinion-text");
        SetBearer(client, owner.AccessToken);
        var before = await SideEffectCountsAsync();

        using var request = await client.PostAsJsonAsync(
            "/api/v1/ai/second-opinions",
            new
            {
                patientId = owner.Account.ProfileId,
                text = "Please help me understand a clinician's existing observations."
            });
        var accepted = await request.Content.ReadFromJsonAsync<AcceptedResponse>();
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Equal("succeeded", accepted!.Status);
        Assert.Equal(1, provider.CallCount);

        using var get = await client.GetAsync(accepted.StatusUrl);
        var result = await get.Content.ReadFromJsonAsync<SecondOpinionResponse>();
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("succeeded", result!.Status);
        Assert.Equal("Possible causes could include dehydration.", result.Result!.Summary);
        Assert.Equal(SecondOpinionProductContent.Disclaimer, result.Result.Disclaimer);
        Assert.True(result.Metadata!.AiGenerated);
        Assert.Equal(SecondOpinionProductContent.ResultVersion, result.Metadata.ResultVersion);
        Assert.Equal(SecondOpinionProductContent.DisclaimerVersion,
            result.Metadata.DisclaimerVersion);
        Assert.Equal("phase-106-provider", result.Metadata.Provider);
        Assert.Equal("phase-106-model", result.Metadata.ModelVersion);
        Assert.Equal("ai-second-opinion@v1", result.Metadata.PromptVersion);
        Assert.Equal(before, await SideEffectCountsAsync());

        await using var db = CreateDbContext();
        var analysis = await db.AiAnalysisRequests.AsNoTracking().SingleAsync(
            item => item.Id == EntityId.From(accepted.AnalysisId));
        Assert.Equal(AiAnalysisPurpose.SecondOpinion, analysis.Purpose);
        Assert.Contains("clinician's existing observations",
            analysis.OriginalInputSnapshotJson,
            StringComparison.Ordinal);
        Assert.Contains(owner.Account.ProfileId.ToString("D"),
            analysis.OriginalInputSnapshotJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(await db.AiExecutions.AsNoTracking()
            .Where(item => item.AnalysisRequestId == analysis.Id)
            .ToArrayAsync());
        Assert.Single(await db.AiResultSnapshots.AsNoTracking()
            .Where(item => item.AnalysisRequestId == analysis.Id)
            .ToArrayAsync());
    }

    [Fact]
    public async Task InvalidInputAndTooManyDocuments_Return422WithZeroProviderCalls()
    {
        await EnsureMigratedAsync();
        var provider = Provider.Approved();
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, client, "second-opinion-invalid");
        SetBearer(client, owner.AccessToken);

        using var empty = await client.PostAsJsonAsync(
            "/api/v1/ai/second-opinions",
            new { patientId = owner.Account.ProfileId });
        using var multiple = await client.PostAsJsonAsync(
            "/api/v1/ai/second-opinions",
            new
            {
                patientId = owner.Account.ProfileId,
                documentIds = new[] { Guid.NewGuid(), Guid.NewGuid() }
            });
        using var unsupported = await client.PostAsJsonAsync(
            "/api/v1/ai/second-opinions",
            new
            {
                patientId = owner.Account.ProfileId,
                text = "meaningful health context",
                regenerate = true
            });
        using var tooMuchHistory = await client.PostAsJsonAsync(
            "/api/v1/ai/second-opinions",
            new
            {
                patientId = owner.Account.ProfileId,
                clinicalHistoryEventIds = Enumerable.Range(0, 4)
                    .Select(_ => Guid.NewGuid())
                    .ToArray()
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, multiple.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsupported.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMuchHistory.StatusCode);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task OwnerAndCurrentPatientAuthority_AreConcealedForForeignCaller()
    {
        await EnsureMigratedAsync();
        var provider = Provider.Approved();
        var blobs = new MemoryBlobStore();
        using var factory = Factory(provider, blobs);
        using var ownerClient = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, ownerClient, "second-opinion-owner");
        SetBearer(ownerClient, owner.AccessToken);
        using var foreignClient = factory.CreateApiClient();
        var foreign = await AuthenticateAsync(factory, foreignClient, "second-opinion-foreign");
        SetBearer(foreignClient, foreign.AccessToken);
        var ownerDocument = await UploadTextAsync(
            ownerClient,
            "Owner-only report content.");

        var accepted = await RequestAsync(ownerClient, owner.Account.ProfileId);
        using var foreignGet = await foreignClient.GetAsync(accepted.StatusUrl);
        using var foreignPatient = await ownerClient.PostAsJsonAsync(
            "/api/v1/ai/second-opinions",
            new
            {
                patientId = foreign.Account.ProfileId,
                text = "meaningful health context"
            });
        using var foreignDocument = await foreignClient.PostAsJsonAsync(
            "/api/v1/ai/second-opinions",
            new
            {
                patientId = foreign.Account.ProfileId,
                documentIds = new[] { ownerDocument.DocumentId }
            });
        using var anonymous = await factory.CreateApiClient().GetAsync(accepted.StatusUrl);

        Assert.Equal(HttpStatusCode.NotFound, foreignGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignPatient.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignDocument.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(1, provider.CallCount);
        using var cleanup = await ownerClient.DeleteAsync(
            $"/api/v1/ai/documents/{ownerDocument.DocumentId:D}");
        Assert.Equal(HttpStatusCode.NoContent, cleanup.StatusCode);
    }

    [Fact]
    public async Task SafetyRejectedRawOutput_IsNeverReturnedByPostOrGet()
    {
        await EnsureMigratedAsync();
        const string restricted = "You have diabetes. restricted-second-opinion-marker";
        var provider = Provider.Approved(restricted);
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, client, "second-opinion-rejected");
        SetBearer(client, owner.AccessToken);

        using var post = await client.PostAsJsonAsync(
            "/api/v1/ai/second-opinions",
            new { patientId = owner.Account.ProfileId, text = "Explain this health context." });
        var postBody = await post.Content.ReadAsStringAsync();
        var accepted = JsonSerializer.Deserialize<AcceptedResponse>(postBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var get = await client.GetAsync(accepted!.StatusUrl);
        var getBody = await get.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        Assert.Contains("\"status\":\"rejected\"", postBody, StringComparison.Ordinal);
        Assert.DoesNotContain("restricted-second-opinion-marker", postBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("restricted-second-opinion-marker", getBody,
            StringComparison.Ordinal);
        Assert.Contains(AiSafetyProductContent.Current.GenericFallback,
            getBody,
            StringComparison.Ordinal);

        await using var db = CreateDbContext();
        var validation = await db.AiSafetyValidations.AsNoTracking()
            .SingleAsync(item => item.ExecutionId == EntityId.From(accepted.ExecutionId));
        Assert.Contains("restricted-second-opinion-marker",
            validation.RestrictedAuditOutput,
            StringComparison.Ordinal);
        Assert.False(validation.DisplayEligible);
        Assert.Empty(await db.AiResultSnapshots.AsNoTracking()
            .Where(item => item.ExecutionId == validation.ExecutionId)
            .ToArrayAsync());
    }

    [Fact]
    public async Task OneTemporaryDocument_IsConsumedWithoutExpiryExtensionAndResultSurvivesDeletion()
    {
        await EnsureMigratedAsync();
        var provider = Provider.Approved("The supplied report can be discussed with a doctor.");
        var blobs = new MemoryBlobStore();
        using var factory = Factory(provider, blobs);
        using var client = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, client, "second-opinion-document");
        SetBearer(client, owner.AccessToken);
        var document = await UploadTextAsync(client, "Existing report text for discussion.");

        using var post = await client.PostAsJsonAsync(
            "/api/v1/ai/second-opinions",
            new
            {
                patientId = owner.Account.ProfileId,
                documentIds = new[] { document.DocumentId }
            });
        var accepted = await post.Content.ReadFromJsonAsync<AcceptedResponse>();
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        Assert.Equal(1, provider.CallCount);
        Assert.Contains("Existing report text for discussion.",
            Assert.Single(provider.Requests).UserContent,
            StringComparison.Ordinal);

        DateTimeOffset persistedExpiry;
        await using (var beforeDelete = CreateDbContext())
        {
            var persisted = await beforeDelete.AiUploadedDocuments.AsNoTracking()
                .SingleAsync(item => item.Id == EntityId.From(document.DocumentId));
            persistedExpiry = persisted.ExpiresAt;
            Assert.True(
                (document.ExpiresAt - persistedExpiry).Duration() < TimeSpan.FromMilliseconds(1));
            Assert.Equal(EntityId.From(accepted!.AnalysisId), persisted.AnalysisRequestId);
        }

        using var delete = await client.DeleteAsync(
            $"/api/v1/ai/documents/{document.DocumentId:D}");
        using var get = await client.GetAsync(accepted!.StatusUrl);
        var result = await get.Content.ReadFromJsonAsync<SecondOpinionResponse>();
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("succeeded", result!.Status);
        Assert.Contains("supplied report", result.Result!.Summary, StringComparison.Ordinal);

        await using var afterDelete = CreateDbContext();
        var deleted = await afterDelete.AiUploadedDocuments.AsNoTracking()
            .SingleAsync(item => item.Id == EntityId.From(document.DocumentId));
        Assert.Equal(AiDocumentStatus.Deleted, deleted.Status);
        Assert.Equal(persistedExpiry, deleted.ExpiresAt);
    }

    [Fact]
    public async Task AuthorizedPreTriageAndClinicalHistory_AreCombinedReadOnlyWithProvenance()
    {
        await EnsureMigratedAsync();
        var provider = Provider.Approved("The selected symptom history can be discussed.");
        using var factory = Factory(provider);
        using var client = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, client, "second-opinion-sources");
        SetBearer(client, owner.AccessToken);
        var source = await CompletePreTriageAsync(client, owner.Account.ProfileId);
        var before = await SideEffectCountsAsync();

        using var post = await client.PostAsJsonAsync(
            "/api/v1/ai/second-opinions",
            new
            {
                patientId = owner.Account.ProfileId,
                preTriageSessionId = source.SessionId,
                clinicalHistoryEventIds = new[] { source.EventId }
            });
        var accepted = await post.Content.ReadFromJsonAsync<AcceptedResponse>();

        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        Assert.Equal("succeeded", accepted!.Status);
        Assert.Equal(1, provider.CallCount);
        var providerInput = Assert.Single(provider.Requests).UserContent;
        Assert.Contains("HEADACHE", providerInput, StringComparison.Ordinal);
        Assert.Contains("clinicalHistory", providerInput, StringComparison.Ordinal);
        Assert.Equal(before, await SideEffectCountsAsync());

        await using var db = CreateDbContext();
        var analysis = await db.AiAnalysisRequests.AsNoTracking().SingleAsync(
            item => item.Id == EntityId.From(accepted.AnalysisId));
        Assert.Contains(source.SessionId.ToString("D"),
            analysis.OriginalInputSnapshotJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(source.EventId.ToString("D"),
            analysis.OriginalInputSnapshotJson,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeletedExpiredAndMissingBlobDocuments_AreRejectedBeforeProviderExecution()
    {
        await EnsureMigratedAsync();
        var provider = Provider.Approved();
        var blobs = new MemoryBlobStore();
        using var factory = Factory(provider, blobs);
        using var client = factory.CreateApiClient();
        var owner = await AuthenticateAsync(factory, client, "second-opinion-doc-state");
        SetBearer(client, owner.AccessToken);
        var deleted = await UploadTextAsync(client, "Document that will be deleted.");
        using var delete = await client.DeleteAsync(
            $"/api/v1/ai/documents/{deleted.DocumentId:D}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var now = DateTimeOffset.UtcNow;
        var expired = AiUploadedDocument.Create(
            EntityId.From(owner.Account.AccountId),
            new string('b', 64),
            "text/plain",
            20,
            now.AddDays(-2),
            now.AddDays(-1));
        var missingBlob = AiUploadedDocument.Create(
            EntityId.From(owner.Account.AccountId),
            new string('c', 64),
            "text/plain",
            20,
            now,
            now.AddHours(24));
        await using (var seed = CreateDbContext())
        {
            seed.AddRange(expired, missingBlob);
            await seed.SaveChangesAsync();
        }

        using var deletedRequest = await RequestDocumentAsync(
            client,
            owner.Account.ProfileId,
            deleted.DocumentId);
        using var expiredRequest = await RequestDocumentAsync(
            client,
            owner.Account.ProfileId,
            expired.Id.Value);
        using var missingBlobRequest = await RequestDocumentAsync(
            client,
            owner.Account.ProfileId,
            missingBlob.Id.Value);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, deletedRequest.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, expiredRequest.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missingBlobRequest.StatusCode);
        Assert.Equal(0, provider.CallCount);
    }

    private BeeexyApiFactory Factory(Provider provider, MemoryBlobStore? blobs = null) => new(
        postgres.ConnectionString,
        configureServices: services =>
        {
            services.RemoveAll<IAiProvider>();
            services.AddSingleton<IAiProvider>(provider);
            if (blobs is not null)
            {
                services.RemoveAll<IAiDocumentBlobStore>();
                services.AddSingleton<IAiDocumentBlobStore>(blobs);
            }
        });

    private static async Task<AcceptedResponse> RequestAsync(HttpClient client, Guid patientId)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/ai/second-opinions",
            new { patientId, text = "Please explain this health information." });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AcceptedResponse>())!;
    }

    private static Task<HttpResponseMessage> RequestDocumentAsync(
        HttpClient client,
        Guid patientId,
        Guid documentId) => client.PostAsJsonAsync(
        "/api/v1/ai/second-opinions",
        new { patientId, documentIds = new[] { documentId } });

    private static async Task<DocumentResponse> UploadTextAsync(HttpClient client, string text)
    {
        using var multipart = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        multipart.Add(file, "file", "report.txt");
        using var response = await client.PostAsync("/api/v1/ai/documents", multipart);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DocumentResponse>())!;
    }

    private static async Task<ClinicalSource> CompletePreTriageAsync(
        HttpClient client,
        Guid patientId)
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
        var completed = await complete.Content.ReadFromJsonAsync<CompletedSession>();
        Assert.Equal(HttpStatusCode.Created, complete.StatusCode);
        using var history = await client.GetAsync(
            $"/api/v1/patients/{patientId:D}/clinical-history");
        var page = await history.Content.ReadFromJsonAsync<HistoryPage>();
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var item = Assert.Single(page!.Items,
            candidate => candidate.Source.Id == completed!.EpisodeId);
        return new ClinicalSource(started.SessionId, item.EventId);
    }

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

    private async Task<SideEffectCounts> SideEffectCountsAsync()
    {
        await using var db = CreateDbContext();
        return new SideEffectCounts(
            await db.ClinicalHistoryEvents.CountAsync(),
            await db.ClinicalAmendments.CountAsync(),
            await db.FhirExports.CountAsync(),
            await db.Appointments.CountAsync());
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task EnsureMigratedAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    private sealed class Provider(string summary) : IAiProvider
    {
        private int callCount;
        public int CallCount => callCount;
        public string ProviderIdentifier => "phase-106-provider";
        public string ModelIdentifier => "phase-106-model";
        public ConcurrentQueue<AiProviderRequest> Requests { get; } = new();

        public Task<AiProviderResponse> ExecuteAsync(
            AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            Requests.Enqueue(request);
            return Task.FromResult(new AiProviderResponse(JsonSerializer.Serialize(new
            {
                schemaVersion = "v1",
                summary,
                importantPoints = new[] { "Important point" },
                possibleQuestionsForDoctor = new[] { "What should I discuss?" },
                missingInformation = new[] { "Additional clinical context" },
                disclaimer = SecondOpinionProductContent.Disclaimer
            })));
        }

        public static Provider Approved(string summary = "Educational summary.") => new(summary);
    }

    private sealed class MemoryBlobStore : IAiDocumentBlobStore
    {
        private readonly ConcurrentDictionary<string, byte[]> values = new();
        public Task WritePrivateAsync(
            AiBlobKey key,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            values[key.Value] = content.ToArray();
            return Task.CompletedTask;
        }
        public Task<byte[]> ReadPrivateAsync(
            AiBlobKey key,
            CancellationToken cancellationToken = default) => Task.FromResult(
            values.TryGetValue(key.Value, out var content)
                ? content.ToArray()
                : throw new FileNotFoundException());
        public Task<bool> DeleteAsync(
            AiBlobKey key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(values.TryRemove(key.Value, out _));
        public Task<int> DeleteCreatedBeforeAsync(
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed record SideEffectCounts(int History, int Amendments, int Fhir, int Scheduling);
    private sealed record AuthenticationResult(
        string AccessToken,
        string RefreshToken,
        AuthenticationAccount Account);
    private sealed record AuthenticationAccount(Guid AccountId, Guid ProfileId, string BeeexyId);
    private sealed record AcceptedResponse(
        Guid AnalysisId,
        Guid ExecutionId,
        string Status,
        string StatusUrl);
    private sealed record DocumentResponse(
        Guid DocumentId,
        string ContentType,
        long SizeBytes,
        DateTimeOffset UploadedAt,
        DateTimeOffset ExpiresAt,
        string Status);
    private sealed record StartedSession(Guid SessionId);
    private sealed record CompletedSession(Guid EpisodeId, DateTimeOffset CompletedAt);
    private sealed record HistoryPage(IReadOnlyList<HistoryItem> Items, string? NextCursor);
    private sealed record HistoryItem(Guid EventId, HistorySource Source);
    private sealed record HistorySource(Guid Id);
    private sealed record ClinicalSource(Guid SessionId, Guid EventId);
    private sealed record ResultResponse(
        string Summary,
        IReadOnlyList<string> ImportantPoints,
        IReadOnlyList<string> PossibleQuestionsForDoctor,
        IReadOnlyList<string> MissingInformation,
        string Disclaimer);
    private sealed record MetadataResponse(
        bool AiGenerated,
        DateTimeOffset GeneratedAt,
        string ResultVersion,
        string Provider,
        string ModelVersion,
        string PromptVersion,
        string DisclaimerVersion);
    private sealed record SecondOpinionResponse(
        Guid AnalysisId,
        Guid PatientId,
        Guid? ExecutionId,
        string Status,
        ResultResponse? Result,
        MetadataResponse? Metadata,
        string? SafeMessage);
}
