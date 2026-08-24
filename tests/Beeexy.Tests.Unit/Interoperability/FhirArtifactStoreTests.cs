using Beeexy.Application.Interoperability;
using Beeexy.Infrastructure.Interoperability;

namespace Beeexy.Tests.Unit.Interoperability;

public sealed class FhirArtifactStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"beeexy-fhir-store-{Guid.NewGuid():N}");

    [Fact]
    public async Task Store_WritesExactPrivateBytesAndCanDeleteThem()
    {
        var store = new FileSystemFhirArtifactStore(root);
        var reference = FhirArtifactStorageReference.CreateNew();
        byte[] bytes = [0, 1, 2, 255];

        await store.StoreImmutableAsync(reference, bytes);

        Assert.Equal(bytes, await store.ReadAsync(reference));
        Assert.True(await store.DeleteAsync(reference));
        Assert.False(await store.DeleteAsync(reference));
    }

    [Fact]
    public async Task Store_RejectsOverwriteAndPreservesOriginalBytes()
    {
        var store = new FileSystemFhirArtifactStore(root);
        var reference = FhirArtifactStorageReference.CreateNew();
        byte[] original = [1, 2, 3];
        await store.StoreImmutableAsync(reference, original);

        await Assert.ThrowsAsync<FhirArtifactAlreadyExistsException>(() =>
            store.StoreImmutableAsync(reference, new byte[] { 9, 9, 9 }));

        Assert.Equal(original, await store.ReadAsync(reference));
    }

    [Fact]
    public void Reference_IsOpaquePrivateAndContainsNoSensitiveIdentity()
    {
        var reference = FhirArtifactStorageReference.CreateNew();

        Assert.Equal(64, reference.OpaqueKey.Length);
        Assert.StartsWith("beeexy-private-artifact://local-store/", reference.PrivateUri);
        Assert.DoesNotContain("http", reference.PrivateUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("patient", reference.PrivateUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fhir", reference.OpaqueKey, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(reference,
            FhirArtifactStorageReference.FromPrivateUri(reference.PrivateUri));
        Assert.DoesNotContain(typeof(FhirArtifactStorageReference).GetMethods(), method =>
            method.Name.Contains("Authoriz", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Authenticate", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
