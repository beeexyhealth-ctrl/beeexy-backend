using System.Security.Cryptography;
using Beeexy.Application.Interoperability;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;

namespace Beeexy.Application.Sharing;

public sealed record DownloadExportCommand(
    EntityId ExportArtifactId,
    ShareAccessIdentity? ShareAccess);

public sealed record DownloadExportResult(
    EntityId ExportArtifactId,
    byte[] ArtifactBytes,
    string MediaType,
    string FileName);

public interface IExportDownloadRepository : IAsyncDisposable
{
    Task<ExportArtifact?> FindArtifactAsync(
        EntityId exportArtifactId,
        CancellationToken cancellationToken = default);

    Task<SharedProfileGrantState?> FindGrantForDownloadAsync(
        EntityId shareGrantId,
        CancellationToken cancellationToken = default);

    Task RecordSuccessfulDownloadAsync(
        ShareGrant grant,
        EntityId eventId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public sealed class DownloadExport(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    AuthorizePatientAccess authorizePatientAccess,
    IExportDownloadRepository repository,
    IPrivateArtifactStorage storage,
    ExportArtifactChecksumCalculator checksumCalculator,
    ExportGenerationOptions options)
{
    public async Task<DownloadExportResult> ExecuteAsync(
        DownloadExportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ExportArtifactId.Value == Guid.Empty)
        {
            throw new ExportArtifactNotFoundException();
        }

        var artifact = await repository.FindArtifactAsync(
            command.ExportArtifactId,
            cancellationToken) ?? throw new ExportArtifactNotFoundException();
        ShareGrant? grant = null;
        try
        {
            if (command.ShareAccess is null)
            {
                await AuthorizeBearerAsync(artifact, cancellationToken);
            }
            else
            {
                grant = await AuthorizeShareAsync(
                    artifact,
                    command.ShareAccess,
                    cancellationToken);
            }

            EnsureReadable(artifact);
            byte[] bytes;
            try
            {
                bytes = await storage.ReadAsync(
                    artifact.PrivateStorageIdentity!,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new ExportArtifactContentUnavailableException();
            }

            if (bytes.Length == 0 ||
                bytes.Length > options.MaximumArtifactBytes ||
                !string.Equals(
                    artifact.ChecksumAlgorithm,
                    ExportArtifactChecksumCalculator.Algorithm,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    checksumCalculator.Calculate(bytes),
                    artifact.Checksum,
                    StringComparison.Ordinal))
            {
                throw new ExportArtifactIntegrityException();
            }

            if (grant is not null)
            {
                await repository.RecordSuccessfulDownloadAsync(
                    grant,
                    CreateDownloadEventId(
                        grant.Id,
                        command.ShareAccess!.TokenId,
                        artifact.Id),
                    CurrentInstant(),
                    cancellationToken);
                await repository.CommitAsync(cancellationToken);
            }

            return new DownloadExportResult(
                artifact.Id,
                bytes,
                artifact.MediaType,
                FileName(artifact));
        }
        catch
        {
            if (command.ShareAccess is not null)
            {
                await repository.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }

    public static EntityId CreateDownloadEventId(
        EntityId grantId,
        EntityId tokenId,
        EntityId artifactId)
    {
        Span<byte> input = stackalloc byte[48];
        grantId.Value.TryWriteBytes(input[..16]);
        tokenId.Value.TryWriteBytes(input[16..32]);
        artifactId.Value.TryWriteBytes(input[32..]);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);
        Span<byte> id = stackalloc byte[16];
        hash[..16].CopyTo(id);
        id[7] = (byte)((id[7] & 0x0f) | 0x50);
        id[8] = (byte)((id[8] & 0x3f) | 0x80);
        return EntityId.From(new Guid(id));
    }

    private async Task AuthorizeBearerAsync(
        ExportArtifact artifact,
        CancellationToken cancellationToken)
    {
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var authorization = await authorizePatientAccess.ExecuteAsync(
            artifact.PatientProfileId,
            current,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new ExportArtifactNotFoundException();
        }
    }

    private async Task<ShareGrant> AuthorizeShareAsync(
        ExportArtifact artifact,
        ShareAccessIdentity identity,
        CancellationToken cancellationToken)
    {
        var state = await repository.FindGrantForDownloadAsync(
            identity.ShareGrantId,
            cancellationToken);
        var now = CurrentInstant();
        if (state is null ||
            state.Grant.CreatedAt > now ||
            state.Grant.RevokedAt.HasValue ||
            state.Grant.ExpiresAt <= now ||
            state.Grant.Scope != identity.TokenScope)
        {
            throw new ShareAccessDeniedException();
        }

        if (state.Grant.Scope != ShareScope.SpecificRecords ||
            state.Items.Any(item => item.ShareGrantId != state.Grant.Id) ||
            state.Items.Select(item => new { item.ResourceType, item.ResourceId })
                .Distinct().Count() != state.Items.Count ||
            state.Grant.PatientProfileId != artifact.PatientProfileId ||
            !state.Items.Any(item =>
                item.ResourceType.Value == SupportedShareResourceTypes.ExportArtifact &&
                item.ResourceId == artifact.Id))
        {
            throw new ExportArtifactAccessForbiddenException();
        }

        return state.Grant;
    }

    private static void EnsureReadable(ExportArtifact artifact)
    {
        if (artifact.Status == ExportArtifactStatus.Deleted)
        {
            throw new ExportArtifactContentUnavailableException();
        }

        if (artifact.Status != ExportArtifactStatus.Available ||
            artifact.CompletedAt is null ||
            string.IsNullOrWhiteSpace(artifact.PrivateStorageIdentity) ||
            string.IsNullOrWhiteSpace(artifact.ChecksumAlgorithm) ||
            string.IsNullOrWhiteSpace(artifact.Checksum) ||
            !HasExpectedMediaType(artifact))
        {
            throw new ExportArtifactStateConflictException();
        }
    }

    private static bool HasExpectedMediaType(ExportArtifact artifact) => artifact.Format switch
    {
        ExportArtifactFormat.BeeexyJson => string.Equals(
            artifact.MediaType,
            BeeexyJsonExportRenderer.MediaType,
            StringComparison.Ordinal),
        ExportArtifactFormat.Pdf => string.Equals(
            artifact.MediaType,
            PdfExportContract.MediaType,
            StringComparison.Ordinal),
        ExportArtifactFormat.FhirJson => string.Equals(
            artifact.MediaType,
            FhirR4BaseMvp.MediaType,
            StringComparison.Ordinal),
        _ => false
    };

    private static string FileName(ExportArtifact artifact) => artifact.Format switch
    {
        ExportArtifactFormat.BeeexyJson => $"beeexy-health-export-{artifact.Id.Value:D}.json",
        ExportArtifactFormat.Pdf => $"beeexy-health-export-{artifact.Id.Value:D}.pdf",
        ExportArtifactFormat.FhirJson => $"beeexy-fhir-export-{artifact.Id.Value:D}.json",
        _ => throw new ExportArtifactStateConflictException()
    };

    private DateTimeOffset CurrentInstant()
    {
        var utc = clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}

public sealed class ExportArtifactNotFoundException : Exception;

public sealed class ExportArtifactAccessForbiddenException : Exception;

public sealed class ExportArtifactContentUnavailableException : Exception;

public sealed class ExportArtifactIntegrityException : Exception;
