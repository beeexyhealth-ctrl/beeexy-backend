using Beeexy.Application.Identity;
using Beeexy.Domain.Identity;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Identity;

public sealed class DemoGuestAccountRepository(BeeexyDbContext dbContext)
    : IDemoGuestAccountRepository
{
    public async Task<DemoGuestAccountState> LoadAsync(
        NormalizedEmail email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            candidate => candidate.Email == email,
            cancellationToken);
        if (account is null)
        {
            return new DemoGuestAccountState(null, [], []);
        }

        var profiles = await dbContext.PatientProfiles
            .Where(profile => profile.AccountId == account.Id)
            .ToListAsync(cancellationToken);
        var preferences = await dbContext.UserPreferences
            .Where(preference => preference.AccountId == account.Id)
            .ToListAsync(cancellationToken);

        return new DemoGuestAccountState(account, profiles, preferences);
    }
}
