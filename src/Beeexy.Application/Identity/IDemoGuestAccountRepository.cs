using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Identity;

public interface IDemoGuestAccountRepository
{
    Task<DemoGuestAccountState> LoadAsync(
        NormalizedEmail email,
        CancellationToken cancellationToken = default);
}

public sealed record DemoGuestAccountState(
    Account? Account,
    IReadOnlyList<PatientProfile> PrimaryProfiles,
    IReadOnlyList<UserPreference> Preferences);
