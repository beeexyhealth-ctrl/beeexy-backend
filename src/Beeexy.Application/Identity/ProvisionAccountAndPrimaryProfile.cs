using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Identity;

public sealed class ProvisionAccountAndPrimaryProfile(
    IAccountProvisioningRepository repository)
{
    private static readonly UserTimeZone InitialTimeZone = UserTimeZone.Create("Etc/UTC");

    public async Task<ProvisionedAccountResult> ExecuteAsync(
        NormalizedEmail email,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        if (createdAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be UTC.", nameof(createdAt));
        }

        // This transaction-scoped PostgreSQL advisory lock serializes every first-login
        // provisioning attempt for the same normalized identity. Database unique indexes
        // remain the final authority for account/profile uniqueness.
        await repository.AcquireEmailLockAsync(email, cancellationToken);

        var existingAccount = await repository.FindAccountAsync(email, cancellationToken);
        if (existingAccount is not null)
        {
            if (existingAccount.Status != AccountStatus.Active)
            {
                throw new AccountAuthenticationRejectedException();
            }

            var existingProfile = await repository.FindPrimaryProfileAsync(
                existingAccount,
                cancellationToken);
            if (existingProfile is null)
            {
                throw new IdentityProvisioningInvariantException();
            }

            return new ProvisionedAccountResult(existingAccount, existingProfile, false);
        }

        var account = Account.Create(email, createdAt);
        var profileId = EntityId.New();
        var beeexyId = BeeexyId.Create(
            $"BXY-{profileId.Value:N}".ToUpperInvariant());
        var profile = PatientProfile.Create(beeexyId, createdAt, account.Id, profileId);
        var preference = UserPreference.Create(account.Id, InitialTimeZone, createdAt);

        repository.Add(account, profile, preference);
        return new ProvisionedAccountResult(account, profile, true);
    }
}

public sealed record ProvisionedAccountResult(
    Account Account,
    PatientProfile PrimaryProfile,
    bool WasProvisioned);
