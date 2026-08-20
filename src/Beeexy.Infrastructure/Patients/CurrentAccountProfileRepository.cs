using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Patients;

public sealed class CurrentAccountProfileRepository(BeeexyDbContext dbContext)
    : ICurrentAccountProfileRepository
{
    public async Task<CurrentAccountProfileState> LoadAsync(
        EntityId accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            candidate => candidate.Id == accountId,
            cancellationToken);
        var profiles = await dbContext.PatientProfiles
            .Where(profile => profile.AccountId == accountId)
            .Take(2)
            .ToListAsync(cancellationToken);
        var preferences = await dbContext.UserPreferences
            .Where(preference => preference.AccountId == accountId)
            .Take(2)
            .ToListAsync(cancellationToken);

        return new CurrentAccountProfileState(account, profiles, preferences);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ProfileUpdateConcurrencyException();
        }
    }
}
