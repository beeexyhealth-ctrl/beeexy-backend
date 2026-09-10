using System.Security.Cryptography;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;

namespace Beeexy.Application.Sharing;

public sealed class BuildSharedProfile(
    ISharedProfileGrantRepository grantRepository,
    IShareScopeEvaluator scopeEvaluator,
    ICanonicalSharedHealthSnapshotBuilder snapshotBuilder,
    IShareAccessEventRecorder eventRecorder,
    IClock clock)
{
    public async Task<BuildSharedProfileResult> ExecuteAsync(
        ShareAccessIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var state = await grantRepository.FindAsync(
            identity.ShareGrantId,
            cancellationToken);
        var now = CurrentInstant();
        if (state is null ||
            state.Grant.CreatedAt > now ||
            state.Grant.RevokedAt.HasValue ||
            state.Grant.ExpiresAt <= now)
        {
            throw new ShareAccessDeniedException();
        }

        var selection = scopeEvaluator.Evaluate(
            state.Grant,
            state.Items,
            identity.TokenScope);
        CanonicalSharedHealthSnapshot profile;
        try
        {
            profile = await snapshotBuilder.BuildAsync(
                state.Grant.PatientProfileId,
                selection,
                cancellationToken);
        }
        catch (SharedProfileSourceUnavailableException)
        {
            throw new ShareAccessDeniedException();
        }

        await eventRecorder.RecordSuccessfulAccessAsync(
            state.Grant,
            CreateAccessEventId(state.Grant.Id, identity.TokenId),
            now,
            cancellationToken);
        return new BuildSharedProfileResult(selection.Scope, profile);
    }

    public static EntityId CreateAccessEventId(EntityId grantId, EntityId tokenId)
    {
        Span<byte> input = stackalloc byte[32];
        grantId.Value.TryWriteBytes(input[..16]);
        tokenId.Value.TryWriteBytes(input[16..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        Span<byte> id = stackalloc byte[16];
        hash[..16].CopyTo(id);
        id[7] = (byte)((id[7] & 0x0f) | 0x50);
        id[8] = (byte)((id[8] & 0x3f) | 0x80);
        return EntityId.From(new Guid(id));
    }

    private DateTimeOffset CurrentInstant()
    {
        var utc = clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}

public sealed class SharedProfileSourceUnavailableException : Exception;
