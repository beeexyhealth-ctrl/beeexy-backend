using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Identity;

public interface IExternalIdentityAuthenticationRepository
{
    Task AcquireIdentityLockAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default);

    Task<ExternalIdentity?> FindIdentityAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default);

    Task<Account?> FindAccountAsync(
        EntityId accountId,
        CancellationToken cancellationToken = default);

    Task<Account?> FindAccountAsync(
        NormalizedEmail email,
        CancellationToken cancellationToken = default);

    Task<PatientProfile?> FindPrimaryProfileAsync(
        EntityId accountId,
        CancellationToken cancellationToken = default);

    void Add(ExternalIdentity identity);
}
