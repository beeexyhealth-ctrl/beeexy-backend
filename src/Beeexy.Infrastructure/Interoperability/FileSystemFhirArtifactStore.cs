using Beeexy.Application.Interoperability;

namespace Beeexy.Infrastructure.Interoperability;

internal sealed class FileSystemFhirArtifactStore : IFhirArtifactStore
{
    private const string ArtifactExtension = ".snapshot";
    private readonly string rootDirectory;

    public FileSystemFhirArtifactStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public async Task StoreImmutableAsync(
        FhirArtifactStorageReference reference,
        ReadOnlyMemory<byte> artifactBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        Directory.CreateDirectory(rootDirectory);
        var artifactPath = GetArtifactPath(reference);
        var temporaryPath = Path.Combine(
            rootDirectory,
            $".{reference.OpaqueKey}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(artifactBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            try
            {
                File.Move(temporaryPath, artifactPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(artifactPath))
            {
                throw new FhirArtifactAlreadyExistsException();
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<byte[]> ReadAsync(
        FhirArtifactStorageReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return File.ReadAllBytesAsync(GetArtifactPath(reference), cancellationToken);
    }

    public Task<bool> DeleteAsync(
        FhirArtifactStorageReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var path = GetArtifactPath(reference);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    private string GetArtifactPath(FhirArtifactStorageReference reference)
    {
        var fileName = reference.OpaqueKey + ArtifactExtension;
        var path = Path.GetFullPath(Path.Combine(rootDirectory, fileName));
        if (!string.Equals(
            Path.GetDirectoryName(path),
            rootDirectory,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The private artifact path escaped its store.");
        }

        return path;
    }
}
