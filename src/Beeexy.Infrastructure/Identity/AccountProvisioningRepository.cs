using Beeexy.Application.Identity;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Identity;

public sealed class AccountProvisioningRepository(BeeexyDbContext dbContext)
    : IAccountProvisioningRepository
{
    private const long AdvisoryLockNamespace = 2303;

    public async Task AcquireEmailLockAsync(
        NormalizedEmail email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({email.Value}, {AdvisoryLockNamespace}))",
            cancellationToken);
    }

    public Task<Account?> FindAccountAsync(
        NormalizedEmail email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        return dbContext.Accounts.SingleOrDefaultAsync(
            account => account.Email == email,
            cancellationToken);
    }

    public Task<PatientProfile?> FindPrimaryProfileAsync(
        Account account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        return dbContext.PatientProfiles.SingleOrDefaultAsync(
            profile => profile.AccountId == account.Id,
            cancellationToken);
    }

    public void Add(Account account, PatientProfile profile, UserPreference preference)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(preference);

        dbContext.AddRange(account, profile, preference);
    }
}
