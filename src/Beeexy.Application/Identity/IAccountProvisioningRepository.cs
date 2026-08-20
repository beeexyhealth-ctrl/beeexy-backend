using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Identity;

public interface IAccountProvisioningRepository
{
    Task AcquireEmailLockAsync(
        NormalizedEmail email,
        CancellationToken cancellationToken = default);

    Task<Account?> FindAccountAsync(
        NormalizedEmail email,
        CancellationToken cancellationToken = default);

    Task<PatientProfile?> FindPrimaryProfileAsync(
        Account account,
        CancellationToken cancellationToken = default);

    void Add(Account account, PatientProfile profile, UserPreference preference);
}
