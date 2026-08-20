using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public sealed class UpdatePrimaryProfile(
    IClock clock,
    CurrentAccountProfileResolver resolver,
    ICurrentAccountProfileRepository repository,
    IIdentityVerificationTransaction transaction,
    IAccountProfileAuditLogger auditLogger)
{
    public async Task<PrimaryProfileResult> ExecuteAsync(
        UpdatePrimaryProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var requestedTimeZone = Validate(command);
        var now = clock.UtcNow;

        await transaction.BeginAsync(cancellationToken);
        var current = await resolver.ResolveAsync(cancellationToken);
        if (current.Preference.Version != command.ExpectedVersion)
        {
            auditLogger.ProfileUpdateConflict(
                current.Account.Id,
                current.PrimaryProfile.Id);
            throw new ProfileUpdateConcurrencyException();
        }

        var changedFields = new List<string>(1);
        if (requestedTimeZone is not null && current.Preference.TimeZone != requestedTimeZone)
        {
            current.Preference.ChangeTimeZone(requestedTimeZone, now);
            changedFields.Add("timezone");
        }

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (ProfileUpdateConcurrencyException)
        {
            auditLogger.ProfileUpdateConflict(
                current.Account.Id,
                current.PrimaryProfile.Id);
            throw;
        }

        await transaction.CommitAsync(cancellationToken);
        auditLogger.ProfileUpdateSucceeded(
            current.Account.Id,
            current.PrimaryProfile.Id,
            changedFields,
            now);
        return GetPrimaryProfile.ToResult(current);
    }

    private static UserTimeZone? Validate(UpdatePrimaryProfileCommand command)
    {
        if (command.ExpectedVersion <= 0)
        {
            throw new RequestValidationException(
                "profile.invalid_version",
                "A positive profile version is required.");
        }

        if (command.Timezone is null)
        {
            return null;
        }

        try
        {
            return UserTimeZone.Create(command.Timezone);
        }
        catch (ArgumentException)
        {
            throw new RequestValidationException(
                "profile.invalid_timezone",
                "The timezone must be a recognized IANA identifier.");
        }
    }
}

public sealed record UpdatePrimaryProfileCommand(string? Timezone, long ExpectedVersion);
