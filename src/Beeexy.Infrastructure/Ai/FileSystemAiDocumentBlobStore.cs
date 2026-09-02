using Beeexy.Application.Ai;

namespace Beeexy.Infrastructure.Ai;

internal sealed class FileSystemAiDocumentBlobStore : IAiDocumentBlobStore
{
    private const string BlobExtension = ".blob";
    private readonly string rootDirectory;

    public FileSystemAiDocumentBlobStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public async Task WritePrivateAsync(
        AiBlobKey key,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        Directory.CreateDirectory(rootDirectory);
        var destination = GetPath(key);
        var temporary = Path.Combine(rootDirectory, $".{key.Value}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public Task<byte[]> ReadPrivateAsync(
        AiBlobKey key,
        CancellationToken cancellationToken = default) =>
        File.ReadAllBytesAsync(GetPath(key), cancellationToken);

    public Task<bool> DeleteAsync(
        AiBlobKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(key);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    public Task<int> DeleteCreatedBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return Task.FromResult(0);
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(rootDirectory, "*" + BlobExtension))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(file);
            if (!TryParseOpaqueKey(name) || File.GetLastWriteTimeUtc(file) > cutoff.UtcDateTime)
            {
                continue;
            }

            File.Delete(file);
            deleted++;
        }

        return Task.FromResult(deleted);
    }

    private string GetPath(AiBlobKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var path = Path.GetFullPath(Path.Combine(rootDirectory, key.Value + BlobExtension));
        if (!string.Equals(Path.GetDirectoryName(path), rootDirectory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The private blob path escaped its store.");
        }

        return path;
    }

    private static bool TryParseOpaqueKey(string value)
    {
        try
        {
            AiBlobKey.Parse(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
