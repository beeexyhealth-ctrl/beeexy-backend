using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Identity;

public interface IRefreshSessionRepository
{
    void Add(RefreshSession session);

    Task<RefreshSession?> FindByTokenHashForUpdateAsync(
        TokenHash tokenHash,
        CancellationToken cancellationToken = default);

    Task<RefreshSession?> FindByIdForUpdateAsync(
        EntityId sessionId,
        CancellationToken cancellationToken = default);

    Task<Account?> FindAccountAsync(
        EntityId accountId,
        CancellationToken cancellationToken = default);

    Task<PatientProfile?> FindPrimaryProfileAsync(
        EntityId accountId,
        CancellationToken cancellationToken = default);

    Task RevokeFamilyAsync(
        EntityId familyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);
}
