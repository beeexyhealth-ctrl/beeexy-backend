using System.Security.Cryptography;

namespace Beeexy.Application.Interoperability;

public sealed record FhirArtifactStorageReference
{
    public const string Scheme = "beeexy-private-artifact";
    public const string Host = "local-store";
    public const int OpaqueKeyByteLength = 32;

    private FhirArtifactStorageReference(string opaqueKey)
    {
        OpaqueKey = opaqueKey;
        PrivateUri = $"{Scheme}://{Host}/{opaqueKey}";
    }

    public string OpaqueKey { get; }

    public string PrivateUri { get; }

    public static FhirArtifactStorageReference CreateNew()
    {
        return new FhirArtifactStorageReference(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(OpaqueKeyByteLength))
                .ToLowerInvariant());
    }

    public static FhirArtifactStorageReference FromPrivateUri(string privateUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateUri);
        if (!Uri.TryCreate(privateUri, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.Ordinal) ||
            !string.Equals(uri.Host, Host, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "The private artifact reference is invalid.",
                nameof(privateUri));
        }

        var opaqueKey = uri.AbsolutePath.Trim('/');
        if (opaqueKey.Length != OpaqueKeyByteLength * 2 ||
            opaqueKey.Any(character => !IsLowerHex(character)))
        {
            throw new ArgumentException(
                "The private artifact reference is invalid.",
                nameof(privateUri));
        }

        return new FhirArtifactStorageReference(opaqueKey);
    }

    private static bool IsLowerHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';
}

public interface IFhirArtifactStore
{
    Task StoreImmutableAsync(
        FhirArtifactStorageReference reference,
        ReadOnlyMemory<byte> artifactBytes,
        CancellationToken cancellationToken = default);

    Task<byte[]> ReadAsync(
        FhirArtifactStorageReference reference,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        FhirArtifactStorageReference reference,
        CancellationToken cancellationToken = default);
}

public sealed class FhirArtifactAlreadyExistsException : Exception
{
    public FhirArtifactAlreadyExistsException()
        : base("The immutable private artifact already exists.")
    {
    }
}
