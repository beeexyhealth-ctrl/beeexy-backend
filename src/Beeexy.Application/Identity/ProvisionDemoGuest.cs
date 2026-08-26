using Beeexy.Domain.Common;

namespace Beeexy.Application.Identity;

public sealed class ProvisionDemoGuest(
    IClock clock,
    IIdentityVerificationTransaction transaction,
    ProvisionAccountAndPrimaryProfile provisionAccount,
    IDemoGuestAccountRepository repository)
{
    public async Task<ProvisionDemoGuestResult> ExecuteAsync(
        DemoGuestDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var now = clock.UtcNow;

        await transaction.BeginAsync(cancellationToken);

        ProvisionedAccountResult identity;
        try
        {
            identity = await provisionAccount.ExecuteAsync(
                definition.Email,
                now,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is AccountAuthenticationRejectedException or
            IdentityProvisioningInvariantException)
        {
            throw new DemoGuestProvisioningConflictException();
        }

        await transaction.SaveChangesAsync(cancellationToken);
        var state = await repository.LoadAsync(definition.Email, cancellationToken);

        if (identity.WasProvisioned)
        {
            if (state.PrimaryProfiles.Count != 1 || state.Preferences.Count != 1)
            {
                throw new DemoGuestProvisioningConflictException();
            }

            state.PrimaryProfiles[0].UpdateDemographics(
                definition.FirstName,
                definition.LastName,
                definition.DateOfBirth,
                definition.SexAssignedAtBirth,
                definition.State,
                now);
            state.Preferences[0].ChangeTimeZone(definition.TimeZone, now);
            await transaction.SaveChangesAsync(cancellationToken);
        }

        var resolved = DemoGuestAccountResolver.TryResolve(definition, state);
        if (resolved is null ||
            resolved.Account.Id != identity.Account.Id ||
            resolved.PrimaryProfile.Id != identity.PrimaryProfile.Id)
        {
            throw new DemoGuestProvisioningConflictException();
        }

        await transaction.CommitAsync(cancellationToken);
        return new ProvisionDemoGuestResult(
            resolved.Account.Id,
            resolved.PrimaryProfile.Id,
            resolved.PrimaryProfile.BeeexyId.Value,
            identity.WasProvisioned);
    }
}

public sealed record ProvisionDemoGuestResult(
    Beeexy.Domain.Common.EntityId AccountId,
    Beeexy.Domain.Common.EntityId ProfileId,
    string BeeexyId,
    bool WasProvisioned);
