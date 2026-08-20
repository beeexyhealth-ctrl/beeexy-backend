using Beeexy.Application.Identity;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public sealed class CurrentAccountProfileResolver(
    ICurrentSessionIdentity currentSessionIdentity,
    ICurrentAccountProfileRepository repository,
    IAccountProfileAuditLogger auditLogger)
{
    public async Task<ResolvedCurrentAccountProfile> ResolveAsync(
        CancellationToken cancellationToken = default)
    {
        var current = currentSessionIdentity.GetRequired();
        var state = await repository.LoadAsync(current.AccountId, cancellationToken);

        if (state.Account is null || state.Account.Status != AccountStatus.Active)
        {
            throw new SessionAuthenticationException();
        }

        if (state.Profiles.Count != 1)
        {
            auditLogger.InvariantViolation(current.AccountId, "primary-profile-count");
            throw new AccountProfileInvariantException();
        }

        if (state.Preferences.Count != 1)
        {
            auditLogger.InvariantViolation(current.AccountId, "preference-count");
            throw new AccountProfileInvariantException();
        }

        return new ResolvedCurrentAccountProfile(
            state.Account,
            state.Profiles[0],
            state.Preferences[0]);
    }
}

public sealed record ResolvedCurrentAccountProfile(
    Account Account,
    PatientProfile PrimaryProfile,
    UserPreference Preference);
