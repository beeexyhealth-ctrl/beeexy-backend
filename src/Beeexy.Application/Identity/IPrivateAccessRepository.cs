using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Identity;

public interface IPrivateAccessRepository
{
    Task<PrivateAccessCredential?> FindCredentialAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task<PrivateAccessCredential?> FindCredentialForUpdateAsync(
        EntityId credentialId,
        CancellationToken cancellationToken = default);

    Task<PrivateAccessAccountState> LoadAccountStateAsync(
        EntityId accountId,
        CancellationToken cancellationToken = default);

    Task<PrivateAccessSessionState?> FindSessionAsync(
        TokenHash tokenHash,
        CancellationToken cancellationToken = default);

    Task<PrivateAccessSessionState?> FindSessionForUpdateAsync(
        TokenHash tokenHash,
        CancellationToken cancellationToken = default);

    void Add(PrivateAccessSession session);

    Task RevokeRefreshFamilyAsync(
        EntityId familyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);
}

public sealed record PrivateAccessAccountState(
    Account? Account,
    IReadOnlyList<PatientProfile> Profiles,
    IReadOnlyList<UserPreference> Preferences);

public sealed record PrivateAccessSessionState(
    PrivateAccessSession Session,
    PrivateAccessCredential Credential,
    Account? Account);
