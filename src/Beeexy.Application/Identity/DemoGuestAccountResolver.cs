using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Identity;

public static class DemoGuestAccountResolver
{
    public static ResolvedDemoGuestAccount? TryResolve(
        DemoGuestDefinition definition,
        DemoGuestAccountState state)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);

        if (state.Account is not { Status: AccountStatus.Active } account ||
            account.Email != definition.Email ||
            state.PrimaryProfiles.Count != 1 ||
            state.Preferences.Count != 1)
        {
            return null;
        }

        var profile = state.PrimaryProfiles[0];
        var preference = state.Preferences[0];
        if (profile.AccountId != account.Id ||
            preference.AccountId != account.Id ||
            profile.FirstName != definition.FirstName ||
            profile.LastName != definition.LastName ||
            profile.DateOfBirth != definition.DateOfBirth ||
            profile.SexAssignedAtBirth != definition.SexAssignedAtBirth ||
            profile.State != definition.State ||
            preference.TimeZone != definition.TimeZone)
        {
            return null;
        }

        return new ResolvedDemoGuestAccount(account, profile, preference);
    }
}

public sealed record ResolvedDemoGuestAccount(
    Account Account,
    PatientProfile PrimaryProfile,
    UserPreference Preference);
