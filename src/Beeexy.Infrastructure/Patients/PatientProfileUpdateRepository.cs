using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Patients;

public sealed class PatientProfileUpdateRepository(BeeexyDbContext dbContext)
    : IPatientProfileUpdateRepository
{
    public Task<PatientProfile?> FindAsync(
        EntityId profileId,
        CancellationToken cancellationToken = default) =>
        dbContext.PatientProfiles.SingleOrDefaultAsync(
            profile => profile.Id == profileId,
            cancellationToken);

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
