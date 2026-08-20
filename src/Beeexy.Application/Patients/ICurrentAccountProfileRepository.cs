using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public interface ICurrentAccountProfileRepository
{
    Task<CurrentAccountProfileState> LoadAsync(
        EntityId accountId,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record CurrentAccountProfileState(
    Account? Account,
    IReadOnlyList<PatientProfile> Profiles,
    IReadOnlyList<UserPreference> Preferences);
