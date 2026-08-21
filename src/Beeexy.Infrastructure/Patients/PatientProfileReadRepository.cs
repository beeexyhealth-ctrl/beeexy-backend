using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Patients;

public sealed class PatientProfileReadRepository(BeeexyDbContext dbContext)
    : IPatientProfileReadRepository
{
    public async Task<PatientProfileReadRecord?> FindAsync(
        EntityId profileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.PatientProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == profileId,
                cancellationToken);

        return profile is null
            ? null
            : new PatientProfileReadRecord(profile.Id, profile.BeeexyId.Value);
    }
}
