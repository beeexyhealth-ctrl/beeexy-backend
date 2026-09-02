using System.Text;
using Beeexy.Application.Ai;
using Beeexy.Application.Identity;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beeexy.Tests.Unit.Ai;

[Trait("Category", "Phase105")]
[Trait("Category", "Phase108")]
public sealed class AiDocumentUseCasesTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 15, 0, 0, TimeSpan.Zero);
    private static readonly EntityId Owner = EntityId.New();

    [Fact]
    public async Task ValidTxt_IsStoredPrivatelyAndMetadataExpiresAtExactly24Hours()
    {
        var fixture = new Fixture();
        var bytes = "Useful medical notes"u8.ToArray();

        var result = await fixture.Upload.ExecuteAsync(new(
            "notes.txt", "text/plain; charset=utf-8", bytes.Length, bytes));

        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal(bytes.Length, result.SizeBytes);
        Assert.Equal(Now.AddHours(24), result.ExpiresAt);
        Assert.Equal(AiDocumentStatus.Active, result.Status);
        var document = Assert.Single(fixture.Repository.Documents);
        Assert.Equal(Owner, document.AccountId);
        Assert.Equal(bytes, fixture.Blobs.Content[document.StorageKey]);
        Assert.DoesNotContain(document.StorageKey, result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("notes.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("notes.csv", "text/csv")]
    [InlineData("notes.jpg", "image/jpeg")]
    [InlineData("notes.txt", "application/octet-stream")]
    public async Task UnsupportedTypes_AreRejectedBeforeScanningOrStorage(
        string fileName,
        string contentType)
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<AiDocumentUnsupportedMediaException>(() =>
            fixture.Upload.ExecuteAsync(new(fileName, contentType, 5, "hello"u8.ToArray())));

        Assert.Equal(0, fixture.Scanner.Calls);
        Assert.Empty(fixture.Blobs.Content);
        Assert.Empty(fixture.Repository.Documents);
    }

    [Theory]
    [InlineData("renamed.pdf", "application/pdf", "plain text")]
    [InlineData("renamed.txt", "text/plain", "%PDF-1.7")]
    [InlineData("renamed.txt", "application/pdf", "%PDF-1.7")]
    public async Task MimeExtensionOrSignatureMismatch_IsUnsupported(
        string fileName,
        string contentType,
        string content)
    {
        var fixture = new Fixture();
        var bytes = Encoding.ASCII.GetBytes(content);
        await Assert.ThrowsAsync<AiDocumentUnsupportedMediaException>(() =>
            fixture.Upload.ExecuteAsync(new(fileName, contentType, bytes.Length, bytes)));
    }

    [Fact]
    public async Task AboveLimit_Is413CategoryBeforeValidation()
    {
        var fixture = new Fixture(options: new AiDocumentOptions(5, TimeSpan.FromMinutes(1), 10));
        await Assert.ThrowsAsync<AiDocumentTooLargeException>(() =>
            fixture.Upload.ExecuteAsync(new("a.txt", "text/plain", 6, "123456"u8.ToArray())));
        Assert.Equal(0, fixture.Scanner.Calls);
    }

    [Fact]
    public async Task ExactConfiguredBoundary_IsAccepted()
    {
        var fixture = new Fixture(options: new AiDocumentOptions(5, TimeSpan.FromMinutes(1), 10));
        var result = await fixture.Upload.ExecuteAsync(
            new("a.txt", "text/plain", 5, "abcde"u8.ToArray()));
        Assert.Equal(5, result.SizeBytes);
    }

    [Fact]
    public async Task ExactTwentyFiveMebibyteBoundary_IsAccepted()
    {
        var fixture = new Fixture();
        var content = GC.AllocateUninitializedArray<byte>(
            (int)AiDocumentOptions.MaximumAllowedBytes);
        content.AsSpan().Fill((byte)'a');
        var result = await fixture.Upload.ExecuteAsync(new(
            "boundary.txt", "text/plain", content.Length, content));
        Assert.Equal(AiDocumentOptions.MaximumAllowedBytes, result.SizeBytes);
    }

    [Fact]
    public async Task UnsafeScannerResult_LeavesNoArtifact()
    {
        var fixture = new Fixture();
        fixture.Scanner.Result = new(AiFileSafetyStatus.Unsafe);
        var exception = await Assert.ThrowsAsync<AiDocumentValidationException>(() =>
            fixture.Upload.ExecuteAsync(new("a.txt", "text/plain", 5, "hello"u8.ToArray())));
        Assert.Equal("ai.document.file_unsafe", exception.Code);
        Assert.Empty(fixture.Blobs.Content);
    }

    [Fact]
    public async Task ScannerFailure_FailsClosedAndLeavesNoArtifact()
    {
        var fixture = new Fixture();
        fixture.Scanner.Failure = new IOException();
        await Assert.ThrowsAsync<AiDocumentValidationException>(() =>
            fixture.Upload.ExecuteAsync(new("a.txt", "text/plain", 5, "hello"u8.ToArray())));
        Assert.Empty(fixture.Blobs.Content);
    }

    [Theory]
    [InlineData(AiDocumentExtractionStatus.NoUsefulText)]
    [InlineData(AiDocumentExtractionStatus.Malformed)]
    [InlineData(AiDocumentExtractionStatus.Failed)]
    [InlineData(AiDocumentExtractionStatus.Unsupported)]
    public async Task UnusableExtraction_Is422AndLeavesNoArtifact(
        AiDocumentExtractionStatus status)
    {
        var fixture = new Fixture();
        fixture.Extractor.Result = new(status);
        var exception = await Assert.ThrowsAsync<AiDocumentValidationException>(() =>
            fixture.Upload.ExecuteAsync(new("a.txt", "text/plain", 5, "hello"u8.ToArray())));
        Assert.Equal("ai.document.unusable_text", exception.Code);
        Assert.Empty(fixture.Blobs.Content);
    }

    [Fact]
    public async Task ExtractionFailure_FailsClosedAndLeavesNoArtifact()
    {
        var fixture = new Fixture();
        fixture.Extractor.Failure = new IOException();
        await Assert.ThrowsAsync<AiDocumentValidationException>(() =>
            fixture.Upload.ExecuteAsync(new("a.txt", "text/plain", 5, "hello"u8.ToArray())));
        Assert.Empty(fixture.Blobs.Content);
    }

    [Fact]
    public async Task PersistenceFailure_CompensatesStoredBlob()
    {
        var fixture = new Fixture();
        fixture.Repository.SaveFailure = new InvalidOperationException();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Upload.ExecuteAsync(
            new("a.txt", "text/plain", 5, "hello"u8.ToArray())));
        Assert.Empty(fixture.Blobs.Content);
        Assert.Equal(1, fixture.Blobs.DeleteCalls);
    }

    [Fact]
    public async Task OwnerDelete_RemovesBlobMarksLifecycleAndIsIdempotent()
    {
        var fixture = new Fixture();
        var created = await fixture.Upload.ExecuteAsync(
            new("a.txt", "text/plain", 5, "hello"u8.ToArray()));

        await fixture.Delete.ExecuteAsync(created.DocumentId);
        await fixture.Delete.ExecuteAsync(created.DocumentId);

        var document = Assert.Single(fixture.Repository.Documents);
        Assert.Equal(AiDocumentStatus.Deleted, document.Status);
        Assert.Equal(Now, document.DeletedAt);
        Assert.Empty(fixture.Blobs.Content);
        Assert.Equal(1, fixture.Blobs.DeleteCalls);
    }

    [Fact]
    public async Task ForeignAndMissingDelete_AreConcealed()
    {
        var fixture = new Fixture();
        var foreign = AiUploadedDocument.Create(
            EntityId.New(), AiBlobKey.CreateNew().Value, "text/plain", 5, Now, Now.AddHours(24));
        fixture.Repository.Documents.Add(foreign);
        await Assert.ThrowsAsync<AiDocumentNotFoundException>(() =>
            fixture.Delete.ExecuteAsync(foreign.Id));
        await Assert.ThrowsAsync<AiDocumentNotFoundException>(() =>
            fixture.Delete.ExecuteAsync(EntityId.New()));
    }

    [Fact]
    public async Task DeleteToleratesMissingBlobAndPersistsLifecycle()
    {
        var fixture = new Fixture();
        var document = fixture.AddActive(Now.AddHours(-1), Now.AddHours(23), addBlob: false);
        await fixture.Delete.ExecuteAsync(document.Id);
        Assert.Equal(AiDocumentStatus.Deleted, document.Status);
    }

    [Fact]
    public async Task ExpiryDeletesOnlyDueArtifactsAndIsRepeatSafe()
    {
        var fixture = new Fixture();
        var expired = fixture.AddActive(Now.AddHours(-25), Now.AddMinutes(-1));
        var atDeadline = fixture.AddActive(Now.AddHours(-24), Now);
        var future = fixture.AddActive(Now, Now.AddHours(24));

        Assert.Equal(2, await fixture.Expire.ExecuteAsync());
        Assert.Equal(0, await fixture.Expire.ExecuteAsync());

        Assert.Equal(AiDocumentStatus.Expired, expired.Status);
        Assert.Equal(AiDocumentStatus.Expired, atDeadline.Status);
        Assert.Equal(AiDocumentStatus.Active, future.Status);
        Assert.True(fixture.Blobs.Content.ContainsKey(future.StorageKey));
    }

    [Fact]
    public async Task ExpiryToleratesAlreadyMissingBlob()
    {
        var fixture = new Fixture();
        var document = fixture.AddActive(Now.AddHours(-25), Now.AddMinutes(-1), false);
        Assert.Equal(1, await fixture.Expire.ExecuteAsync());
        Assert.Equal(AiDocumentStatus.Expired, document.Status);
    }

    [Fact]
    public async Task ExpiryFailure_DoesNotStarveLaterBatchesAndRecoversOnNextRun()
    {
        var fixture = new Fixture(options: new AiDocumentOptions(
            AiDocumentOptions.MaximumAllowedBytes,
            TimeSpan.FromMinutes(1),
            2));
        var documents = Enumerable.Range(0, 5)
            .Select(index => fixture.AddActive(
                Now.AddHours(-30).AddMinutes(index),
                Now.AddHours(-6).AddMinutes(index)))
            .ToArray();
        fixture.Blobs.FailingKeys.Add(documents[0].StorageKey);

        await Assert.ThrowsAsync<AggregateException>(() => fixture.Expire.ExecuteAsync());

        Assert.Equal(AiDocumentStatus.Active, documents[0].Status);
        Assert.All(documents[1..], document =>
            Assert.Equal(AiDocumentStatus.Expired, document.Status));

        fixture.Blobs.FailingKeys.Clear();
        Assert.Equal(1, await fixture.Expire.ExecuteAsync());
        Assert.All(documents, document =>
            Assert.Equal(AiDocumentStatus.Expired, document.Status));
    }

    [Fact]
    public async Task ExpiryWorker_ReportsSafeSuccessAndFailureWithoutSensitiveDetails()
    {
        var fixture = new Fixture();
        fixture.AddActive(Now.AddHours(-25), Now.AddMinutes(-1));
        var logger = new CaptureLogger<AiDocumentExpiryWorker>();
        var services = new ServiceCollection();
        services.AddSingleton(fixture.Expire);
        await using var provider = services.BuildServiceProvider();
        var worker = new AiDocumentExpiryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            fixture.Options,
            new Clock(),
            logger);

        Assert.True(await worker.RunOnceAsync(CancellationToken.None));
        Assert.Contains(logger.Messages, message =>
            message.Contains("removed artifact count 1", StringComparison.Ordinal));

        fixture.Repository.ListFailure = new InvalidOperationException(
            "private-patient-document-marker");
        Assert.False(await worker.RunOnceAsync(CancellationToken.None));
        var logs = string.Join('\n', logger.Messages);
        Assert.Contains("InvalidOperationException", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("private-patient-document-marker", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiryWorker_PropagatesHostCancellationWithoutLoggingFailure()
    {
        var fixture = new Fixture();
        var logger = new CaptureLogger<AiDocumentExpiryWorker>();
        var services = new ServiceCollection();
        services.AddSingleton(fixture.Expire);
        await using var provider = services.BuildServiceProvider();
        var worker = new AiDocumentExpiryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            fixture.Options,
            new Clock(),
            logger);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            worker.RunOnceAsync(cancellation.Token));
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public async Task FailedRun_DoesNotBusyLoopOnOverdueFailureOrMissNextFutureDeadline()
    {
        var fixture = new Fixture(options: new AiDocumentOptions(
            AiDocumentOptions.MaximumAllowedBytes,
            TimeSpan.FromHours(1),
            100));
        fixture.AddActive(Now.AddHours(-25), Now.AddMinutes(-1));
        fixture.AddActive(Now.AddHours(-23), Now.AddHours(1));
        var services = new ServiceCollection();
        services.AddSingleton(fixture.Expire);
        services.AddSingleton<IAiDocumentRepository>(fixture.Repository);
        await using var provider = services.BuildServiceProvider();
        var worker = new AiDocumentExpiryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            fixture.Options,
            new Clock(),
            new CaptureLogger<AiDocumentExpiryWorker>());

        Assert.Equal(
            TimeSpan.FromHours(1),
            await worker.GetNextDelayAfterRunSafelyAsync(
                previousRunSucceeded: false,
                previousRunCutoff: Now,
                cancellationToken: CancellationToken.None));

        fixture.AddActive(Now.AddHours(-23), Now.AddMinutes(5));
        Assert.Equal(
            TimeSpan.FromMinutes(5),
            await worker.GetNextDelayAfterRunSafelyAsync(
                previousRunSucceeded: false,
                previousRunCutoff: Now,
                cancellationToken: CancellationToken.None));
        Assert.Equal(
            TimeSpan.Zero,
            await worker.GetNextDelayAfterRunSafelyAsync(
                previousRunSucceeded: true,
                previousRunCutoff: Now,
                cancellationToken: CancellationToken.None));
    }

    [Fact]
    public void OptionsEnforceExactMaximumAndSaneWorkerBounds()
    {
        _ = new AiDocumentOptions(
            AiDocumentOptions.MaximumAllowedBytes,
            TimeSpan.FromHours(1),
            1_000);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiDocumentOptions(
            AiDocumentOptions.MaximumAllowedBytes + 1,
            TimeSpan.FromMinutes(1),
            100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiDocumentOptions(
            1,
            TimeSpan.FromHours(2),
            100));
    }

    private sealed class Fixture
    {
        public Fixture(AiDocumentOptions? options = null)
        {
            Options = options ?? new AiDocumentOptions(
                AiDocumentOptions.MaximumAllowedBytes,
                TimeSpan.FromMinutes(1),
                100);
            Upload = new UploadAiDocument(
                new Identity(Owner), new Clock(), Options, Scanner, Extractor, Blobs, Repository);
            Delete = new DeleteAiDocument(
                new Identity(Owner), new Clock(), Blobs, Repository);
            Expire = new ExpireAiDocuments(new Clock(), Options, Blobs, Repository);
        }

        public AiDocumentOptions Options { get; }
        public Scanner Scanner { get; } = new();
        public Extractor Extractor { get; } = new();
        public BlobStore Blobs { get; } = new();
        public Repository Repository { get; } = new();
        public UploadAiDocument Upload { get; }
        public DeleteAiDocument Delete { get; }
        public ExpireAiDocuments Expire { get; }

        public AiUploadedDocument AddActive(
            DateTimeOffset created,
            DateTimeOffset expires,
            bool addBlob = true)
        {
            var document = AiUploadedDocument.Create(
                Owner, AiBlobKey.CreateNew().Value, "text/plain", 5, created, expires);
            Repository.Documents.Add(document);
            if (addBlob)
            {
                Blobs.Content[document.StorageKey] = "hello"u8.ToArray();
            }

            return document;
        }
    }

    private sealed class Identity(EntityId accountId) : ICurrentSessionIdentity
    {
        public CurrentSessionIdentity GetRequired() => new(accountId, EntityId.New());
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class Scanner : IAiDocumentSafetyScanner
    {
        public AiFileSafetyResult Result { get; set; } = new(AiFileSafetyStatus.Safe);
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }

        public Task<AiFileSafetyResult> ScanAsync(
            ReadOnlyMemory<byte> content,
            string normalizedContentType,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Failure is null ? Task.FromResult(Result) : Task.FromException<AiFileSafetyResult>(Failure);
        }
    }

    private sealed class Extractor : IAiDocumentTextExtractor
    {
        public AiDocumentExtractionResult Result { get; set; } =
            new(AiDocumentExtractionStatus.Success, "useful text");
        public Exception? Failure { get; set; }

        public Task<AiDocumentExtractionResult> ExtractAsync(
            ReadOnlyMemory<byte> content,
            string normalizedContentType,
            CancellationToken cancellationToken = default) => Failure is null
            ? Task.FromResult(Result)
            : Task.FromException<AiDocumentExtractionResult>(Failure);
    }

    private sealed class BlobStore : IAiDocumentBlobStore
    {
        public Dictionary<string, byte[]> Content { get; } = [];
        public HashSet<string> FailingKeys { get; } = [];
        public int DeleteCalls { get; private set; }

        public Task WritePrivateAsync(AiBlobKey key, ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            Content.Add(key.Value, content.ToArray());
            return Task.CompletedTask;
        }

        public Task<byte[]> ReadPrivateAsync(AiBlobKey key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Content[key.Value]);

        public Task<bool> DeleteAsync(AiBlobKey key,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            if (FailingKeys.Contains(key.Value))
            {
                throw new IOException("private-blob-path-marker");
            }

            return Task.FromResult(Content.Remove(key.Value));
        }

        public Task<int> DeleteCreatedBeforeAsync(DateTimeOffset cutoff,
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class Repository : IAiDocumentRepository
    {
        public List<AiUploadedDocument> Documents { get; } = [];
        public Exception? SaveFailure { get; set; }
        public Exception? ListFailure { get; set; }
        public void Add(AiUploadedDocument document) => Documents.Add(document);
        public Task<AiUploadedDocument?> FindOwnedAsync(EntityId documentId, EntityId accountId,
            CancellationToken cancellationToken = default) => Task.FromResult(Documents
                .SingleOrDefault(document => document.Id == documentId && document.AccountId == accountId));
        public Task<IReadOnlyList<AiUploadedDocument>> ListExpiredAsync(
            DateTimeOffset now,
            int take, AiDocumentExpiryCursor? after = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ListFailure is not null
                ? Task.FromException<IReadOnlyList<AiUploadedDocument>>(ListFailure)
                : Task.FromResult<IReadOnlyList<AiUploadedDocument>>(Documents
                .Where(document => document.Status == AiDocumentStatus.Active && document.ExpiresAt <= now)
                .Where(document => after is null ||
                    document.ExpiresAt > after.ExpiresAt ||
                    (document.ExpiresAt == after.ExpiresAt &&
                     document.Id.Value.CompareTo(after.DocumentId.Value) > 0))
                .OrderBy(document => document.ExpiresAt)
                .ThenBy(document => document.Id.Value)
                .Take(take)
                .ToArray());
        }
        public Task<DateTimeOffset?> GetNextExpiryAsync(
            DateTimeOffset? strictlyAfter = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Documents.Where(document =>
                    document.Status == AiDocumentStatus.Active &&
                    (!strictlyAfter.HasValue || document.ExpiresAt > strictlyAfter.Value))
                .Select(document => (DateTimeOffset?)document.ExpiresAt).Min());
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            SaveFailure is null ? Task.CompletedTask : Task.FromException(SaveFailure);
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
