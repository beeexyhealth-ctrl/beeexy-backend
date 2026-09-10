using System.Text;
using Beeexy.Application.Sharing;
using Beeexy.Infrastructure.Sharing;

namespace Beeexy.Tests.Unit.Sharing;

public sealed class PrivateArtifactStorageTests
{
    [Fact]
    [Trait("Category", "Phase116")]
    public async Task LocalStore_WritesImmutableBytesUnderPrivateRootAndDeletesThem()
    {
        var root = NewRoot();
        try
        {
            var store = new FileSystemPrivateArtifactStorage(root);
            var reference = store.CreateReference();
            var bytes = Encoding.UTF8.GetBytes("private-export");

            await store.StoreImmutableAsync(reference, bytes);

            Assert.Equal(bytes, await store.ReadForVerificationAsync(reference));
            Assert.Equal(root, store.RootDirectory);
            Assert.DoesNotContain("wwwroot", reference.PrivateStorageIdentity,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(root, reference.PrivateStorageIdentity,
                StringComparison.OrdinalIgnoreCase);
            await Assert.ThrowsAsync<PrivateArtifactAlreadyExistsException>(() =>
                store.StoreImmutableAsync(reference, bytes));
            Assert.True(await store.DeleteAsync(reference));
            Assert.False(await store.DeleteAsync(reference));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public async Task LocalStore_RejectsTraversalAndCleansCancelledTemporaryWrite()
    {
        var root = NewRoot();
        try
        {
            var store = new FileSystemPrivateArtifactStorage(root);
            var invalid = new PrivateArtifactStorageReference(
                "../outside",
                "beeexy-private-export://local-store/../outside");
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.StoreImmutableAsync(invalid, Encoding.UTF8.GetBytes("x")));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.StoreImmutableAsync(
                    store.CreateReference(),
                    Encoding.UTF8.GetBytes("x"),
                    cancellation.Token));
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public void LocalStore_RejectsPublicStaticRoot()
    {
        var root = Path.Combine(NewRoot(), "wwwroot", "exports");
        Assert.Throws<ArgumentException>(() =>
            new FileSystemPrivateArtifactStorage(root));
    }

    [Fact]
    [Trait("Category", "Phase116")]
    public async Task ObjectStorageAdapter_UsesOnlyOpaquePrefixedKeys()
    {
        var client = new ObjectStore();
        var options = new PrivateArtifactStorageOptions(
            PrivateArtifactStorageProvider.ObjectStorage,
            null,
            "health/exports");
        var store = new ObjectPrivateArtifactStorage(client, options);
        var reference = store.CreateReference();

        await store.StoreImmutableAsync(reference, Encoding.UTF8.GetBytes("artifact"));

        Assert.Equal($"health/exports/{reference.OpaqueKey}", client.StoredKey);
        Assert.DoesNotContain("..", client.StoredKey, StringComparison.Ordinal);
        Assert.StartsWith("beeexy-private-export://object-store/",
            reference.PrivateStorageIdentity, StringComparison.Ordinal);
        Assert.True(await store.DeleteAsync(reference));
    }

    private static string NewRoot() => Path.Combine(
        Path.GetTempPath(),
        "beeexy-export-tests",
        Guid.NewGuid().ToString("N"));

    private sealed class ObjectStore : IPrivateArtifactObjectStore
    {
        public string StoredKey { get; private set; } = string.Empty;

        public Task StoreImmutableAsync(
            string objectKey,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default)
        {
            StoredKey = objectKey;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(
            string objectKey,
            CancellationToken cancellationToken = default) => Task.FromResult(
            string.Equals(objectKey, StoredKey, StringComparison.Ordinal));
    }
}
