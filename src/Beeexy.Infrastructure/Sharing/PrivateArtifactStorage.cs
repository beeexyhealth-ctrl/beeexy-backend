using System.Security.Cryptography;
using Beeexy.Application.Sharing;

namespace Beeexy.Infrastructure.Sharing;

public enum PrivateArtifactStorageProvider
{
    LocalFileSystem,
    ObjectStorage
}

public sealed record PrivateArtifactStorageOptions(
    PrivateArtifactStorageProvider Provider,
    string? LocalRoot,
    string ObjectKeyPrefix = "exports");

public interface IPrivateArtifactObjectStore
{
    Task StoreImmutableAsync(
        string objectKey,
        ReadOnlyMemory<byte> artifactBytes,
        CancellationToken cancellationToken = default);

    Task<byte[]> ReadAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
}

internal sealed class FileSystemPrivateArtifactStorage : IPrivateArtifactStorage
{
    private const string Scheme = "beeexy-private-export";
    private const string Host = "local-store";
    private const string Extension = ".artifact";
    private readonly string rootDirectory;

    public FileSystemPrivateArtifactStorage(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        if (Path.GetRelativePath(this.rootDirectory, this.rootDirectory) != "." ||
            this.rootDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(value => string.Equals(value, "wwwroot", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "The private artifact root cannot be a public static-files directory.",
                nameof(rootDirectory));
        }
    }

    internal string RootDirectory => rootDirectory;

    public PrivateArtifactStorageReference CreateReference()
    {
        var opaqueKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
        return new PrivateArtifactStorageReference(
            opaqueKey,
            $"{Scheme}://{Host}/{opaqueKey}");
    }

    public async Task StoreImmutableAsync(
        PrivateArtifactStorageReference reference,
        ReadOnlyMemory<byte> artifactBytes,
        CancellationToken cancellationToken = default)
    {
        Validate(reference);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(rootDirectory);
        EnsureNotReparsePoint(rootDirectory);
        var artifactPath = GetArtifactPath(reference);
        var temporaryPath = GetPath($".{reference.OpaqueKey}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(artifactBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporaryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            try
            {
                File.Move(temporaryPath, artifactPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(artifactPath))
            {
                throw new PrivateArtifactAlreadyExistsException();
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

    public Task<bool> DeleteAsync(
        PrivateArtifactStorageReference reference,
        CancellationToken cancellationToken = default)
    {
        Validate(reference);
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetArtifactPath(reference);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        EnsureNotReparsePoint(path);
        File.Delete(path);
        return Task.FromResult(true);
    }

    public Task<byte[]> ReadAsync(
        string privateStorageIdentity,
        CancellationToken cancellationToken = default)
    {
        var reference = ParseIdentity(privateStorageIdentity);
        var path = GetArtifactPath(reference);
        EnsureNotReparsePoint(path);
        return File.ReadAllBytesAsync(path, cancellationToken);
    }

    internal Task<byte[]> ReadForVerificationAsync(
        PrivateArtifactStorageReference reference,
        CancellationToken cancellationToken = default) =>
        ReadAsync(reference.PrivateStorageIdentity, cancellationToken);

    internal PrivateArtifactStorageReference ParseIdentityForVerification(string identity)
        => ParseIdentity(identity);

    private static PrivateArtifactStorageReference ParseIdentity(string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (!Uri.TryCreate(identity, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.Ordinal) ||
            !string.Equals(uri.Host, Host, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("The private artifact identity is invalid.", nameof(identity));
        }

        var reference = new PrivateArtifactStorageReference(
            uri.AbsolutePath.Trim('/'),
            identity);
        Validate(reference);
        return reference;
    }

    private string GetArtifactPath(PrivateArtifactStorageReference reference) =>
        GetPath(reference.OpaqueKey + Extension);

    private string GetPath(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(rootDirectory, fileName));
        if (!string.Equals(
                Path.GetDirectoryName(path),
                rootDirectory,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The private artifact path escaped its store.");
        }

        return path;
    }

    private static void Validate(PrivateArtifactStorageReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.OpaqueKey.Length != 64 ||
            reference.OpaqueKey.Any(value => value is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f')) ||
            !string.Equals(
                reference.PrivateStorageIdentity,
                $"{Scheme}://{Host}/{reference.OpaqueKey}",
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The private artifact reference is invalid.", nameof(reference));
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "A symbolic link cannot be used by private artifact storage.");
        }
    }
}

internal sealed class ObjectPrivateArtifactStorage(
    IPrivateArtifactObjectStore objectStore,
    PrivateArtifactStorageOptions options) : IPrivateArtifactStorage
{
    private const string Scheme = "beeexy-private-export";
    private const string Host = "object-store";
    private readonly string prefix = NormalizePrefix(options.ObjectKeyPrefix);

    public PrivateArtifactStorageReference CreateReference()
    {
        var opaqueKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
        return new PrivateArtifactStorageReference(
            opaqueKey,
            $"{Scheme}://{Host}/{prefix}/{opaqueKey}");
    }

    public Task StoreImmutableAsync(
        PrivateArtifactStorageReference reference,
        ReadOnlyMemory<byte> artifactBytes,
        CancellationToken cancellationToken = default)
    {
        Validate(reference);
        return objectStore.StoreImmutableAsync(
            $"{prefix}/{reference.OpaqueKey}",
            artifactBytes,
            cancellationToken);
    }

    public Task<bool> DeleteAsync(
        PrivateArtifactStorageReference reference,
        CancellationToken cancellationToken = default)
    {
        Validate(reference);
        return objectStore.DeleteAsync(
            $"{prefix}/{reference.OpaqueKey}",
            cancellationToken);
    }

    public Task<byte[]> ReadAsync(
        string privateStorageIdentity,
        CancellationToken cancellationToken = default)
    {
        var reference = ParseIdentity(privateStorageIdentity);
        return objectStore.ReadAsync(
            $"{prefix}/{reference.OpaqueKey}",
            cancellationToken);
    }

    private void Validate(PrivateArtifactStorageReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.OpaqueKey.Length != 64 ||
            reference.OpaqueKey.Any(value => value is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f')) ||
            !string.Equals(
                reference.PrivateStorageIdentity,
                $"{Scheme}://{Host}/{prefix}/{reference.OpaqueKey}",
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The private artifact reference is invalid.", nameof(reference));
        }
    }

    private PrivateArtifactStorageReference ParseIdentity(string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (!Uri.TryCreate(identity, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.Ordinal) ||
            !string.Equals(uri.Host, Host, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("The private artifact identity is invalid.", nameof(identity));
        }

        var path = uri.AbsolutePath.Trim('/');
        var expectedPrefix = prefix + "/";
        if (!path.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("The private artifact identity is invalid.", nameof(identity));
        }

        var reference = new PrivateArtifactStorageReference(
            path[expectedPrefix.Length..],
            identity);
        Validate(reference);
        return reference;
    }

    private static string NormalizePrefix(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim('/');
        if (normalized.Length == 0 ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Any(character => !(char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '/')))
        {
            throw new ArgumentException("The object storage prefix is invalid.", nameof(value));
        }

        return normalized;
    }
}

public sealed class PrivateArtifactAlreadyExistsException : Exception;
