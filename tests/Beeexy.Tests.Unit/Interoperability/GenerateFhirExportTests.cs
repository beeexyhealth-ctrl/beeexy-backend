using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.Interoperability;

namespace Beeexy.Tests.Unit.Interoperability;

public sealed class GenerateFhirExportTests
{
    [Fact]
    public async Task Execute_StoresExactBytesThenPersistsGeneratedMetadata()
    {
        var graph = FhirSnapshotTestData.CreateGraph();
        var transaction = new FakeTransaction(Source(graph));
        var store = new FakeStore();
        var generator = CreateGenerator(transaction, store);

        var result = await generator.ExecuteAsync(Command(graph));

        Assert.True(result.NewlyGenerated);
        Assert.Equal(FhirExportStatus.Generated, result.Export.Status);
        Assert.Equal(FhirSnapshotArtifactFormat.UnresolvedFhirReleaseMarker,
            result.Export.FhirVersion);
        Assert.Equal(FhirArtifactChecksumCalculator.Algorithm,
            result.Export.ChecksumAlgorithm);
        Assert.Equal(
            new FhirArtifactChecksumCalculator().Calculate(store.StoredBytes!),
            result.Export.Checksum);
        Assert.Equal(store.Reference!.PrivateUri,
            result.Export.PrivateArtifactStorageUri);
        Assert.Equal(2, transaction.SaveCount);
        Assert.True(transaction.Committed);
        Assert.Equal(["begin", "load", "find", "add", "save", "save", "commit"],
            transaction.Events);
        Assert.Empty(transaction.ValidationEvents);
    }

    [Fact]
    public async Task Execute_StorageFailureLeavesPendingAndDoesNotCommit()
    {
        var graph = FhirSnapshotTestData.CreateGraph();
        var transaction = new FakeTransaction(Source(graph));
        var store = new FakeStore { ThrowOnStore = true };

        await Assert.ThrowsAsync<IOException>(() =>
            CreateGenerator(transaction, store).ExecuteAsync(Command(graph)));

        Assert.Equal(FhirExportStatus.Pending, transaction.Added!.Status);
        Assert.Null(transaction.Added.Artifact);
        Assert.False(transaction.Committed);
        Assert.Equal(1, transaction.SaveCount);
    }

    [Fact]
    public async Task Execute_PersistenceFailureAfterStorageDeletesArtifact()
    {
        var graph = FhirSnapshotTestData.CreateGraph();
        var transaction = new FakeTransaction(Source(graph)) { ThrowOnSaveNumber = 2 };
        var store = new FakeStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateGenerator(transaction, store).ExecuteAsync(Command(graph)));

        Assert.True(store.DeleteCalled);
        Assert.Null(store.StoredBytes);
        Assert.False(transaction.Committed);
    }

    [Fact]
    public async Task Execute_CleanupFailureRequiresExplicitReconciliation()
    {
        var graph = FhirSnapshotTestData.CreateGraph();
        var transaction = new FakeTransaction(Source(graph)) { ThrowOnSaveNumber = 2 };
        var store = new FakeStore { DeleteResult = false };

        var exception = await Assert.ThrowsAsync<
            FhirArtifactReconciliationRequiredException>(() =>
            CreateGenerator(transaction, store).ExecuteAsync(Command(graph)));

        Assert.IsType<AggregateException>(exception.InnerException);
        Assert.False(transaction.Committed);
    }

    [Fact]
    public async Task Execute_SameIdempotentRequestReturnsExistingWithoutNewArtifact()
    {
        var graph = FhirSnapshotTestData.CreateGraph();
        var command = Command(graph);
        var firstTransaction = new FakeTransaction(Source(graph));
        var first = await CreateGenerator(firstTransaction, new FakeStore())
            .ExecuteAsync(command);
        var secondTransaction = new FakeTransaction(Source(graph))
        {
            Existing = first.Export
        };
        var secondStore = new FakeStore();

        var second = await CreateGenerator(secondTransaction, secondStore)
            .ExecuteAsync(command);

        Assert.False(second.NewlyGenerated);
        Assert.Same(first.Export, second.Export);
        Assert.Null(secondStore.StoredBytes);
        Assert.True(secondTransaction.Committed);
        Assert.Equal(0, secondTransaction.SaveCount);
    }

    [Fact]
    public async Task Execute_ReusedKeyWithDifferentSourceIsRejected()
    {
        var graph = FhirSnapshotTestData.CreateGraph();
        var existingTransaction = new FakeTransaction(Source(graph));
        var existing = (await CreateGenerator(existingTransaction, new FakeStore())
            .ExecuteAsync(Command(graph))).Export;
        var other = FhirSnapshotTestData.CreateGraph();
        var transaction = new FakeTransaction(Source(other)) { Existing = existing };
        var command = Command(other) with { IdempotencyKey = existing.IdempotencyKey };

        await Assert.ThrowsAsync<FhirExportIdempotencyConflictException>(() =>
            CreateGenerator(transaction, new FakeStore()).ExecuteAsync(command));

        Assert.False(transaction.Committed);
    }

    [Fact]
    public async Task GeneratedArtifactAndFrozenMetadataCannotBeReplacedInPlace()
    {
        var graph = FhirSnapshotTestData.CreateGraph();
        var transaction = new FakeTransaction(Source(graph));
        var result = await CreateGenerator(transaction, new FakeStore())
            .ExecuteAsync(Command(graph));
        var artifact = result.Export.Artifact;
        var versions = result.Export.Versions;

        Assert.Throws<InvalidOperationException>(() => result.Export.MarkGenerated(
            FhirArtifactMetadata.Create("SHA-256", new string('f', 64),
                FhirArtifactStorageReference.CreateNew().PrivateUri),
            FhirSnapshotTestData.Utc(19)));
        Assert.Equal(artifact, result.Export.Artifact);
        Assert.Equal(versions, result.Export.Versions);
        Assert.Null(result.Export.ValidationOutcome);
        Assert.Null(result.Export.ValidationCompletedAt);
    }

    private static GenerateFhirExport CreateGenerator(
        FakeTransaction transaction,
        FakeStore store) => new(
            new FixedClock(FhirSnapshotTestData.Utc(18)),
            transaction,
            store,
            new FhirSnapshotSerializer(),
            new FhirArtifactChecksumCalculator());

    private static GenerateFhirExportCommand Command(
        FhirSnapshotTestData.TestGraph graph) => new(
            graph.PatientId,
            graph.HistoryEvent.Id,
            EntityId.New(),
            FhirSnapshotTestData.Specification(),
            "6.5-test-runtime");

    private static FhirExportAuthoritativeSource Source(
        FhirSnapshotTestData.TestGraph graph) => new(
            graph.HistoryEvent,
            graph.Episode,
            graph.Assessment,
            graph.Questionnaire);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeTransaction(FhirExportAuthoritativeSource source)
        : IFhirExportGenerationTransaction
    {
        public FhirExport? Existing { get; init; }

        public FhirExport? Added { get; private set; }

        public int SaveCount { get; private set; }

        public int? ThrowOnSaveNumber { get; init; }

        public bool Committed { get; private set; }

        public List<string> Events { get; } = [];

        public List<string> ValidationEvents { get; } = [];

        public Task BeginAsync(EntityId patientProfileId, EntityId idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Events.Add("begin");
            return Task.CompletedTask;
        }

        public Task<FhirExportAuthoritativeSource?> LoadAuthoritativeSourceAsync(
            EntityId patientProfileId,
            EntityId sourceClinicalHistoryEventId,
            CancellationToken cancellationToken = default)
        {
            Events.Add("load");
            return Task.FromResult<FhirExportAuthoritativeSource?>(source);
        }

        public Task<FhirExport?> FindByIdempotencyKeyAsync(
            EntityId patientProfileId,
            EntityId idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Events.Add("find");
            return Task.FromResult(Existing);
        }

        public void Add(FhirExport export)
        {
            Events.Add("add");
            Added = export;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("save");
            SaveCount++;
            if (SaveCount == ThrowOnSaveNumber)
            {
                throw new InvalidOperationException("simulated persistence failure");
            }

            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("commit");
            Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeStore : IFhirArtifactStore
    {
        public bool ThrowOnStore { get; init; }

        public bool DeleteResult { get; init; } = true;

        public FhirArtifactStorageReference? Reference { get; private set; }

        public byte[]? StoredBytes { get; private set; }

        public bool DeleteCalled { get; private set; }

        public Task StoreImmutableAsync(FhirArtifactStorageReference reference,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnStore)
            {
                throw new IOException("simulated storage failure");
            }

            Reference = reference;
            StoredBytes = artifactBytes.ToArray();
            return Task.CompletedTask;
        }

        public Task<byte[]> ReadAsync(FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StoredBytes ?? throw new FileNotFoundException());

        public Task<bool> DeleteAsync(FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default)
        {
            DeleteCalled = true;
            if (DeleteResult)
            {
                StoredBytes = null;
            }

            return Task.FromResult(DeleteResult);
        }
    }
}
