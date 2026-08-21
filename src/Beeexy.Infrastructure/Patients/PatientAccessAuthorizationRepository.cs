using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Patients;

public sealed class PatientAccessAuthorizationRepository(BeeexyDbContext dbContext)
    : IPatientAccessAuthorizationRepository
{
    public async Task<PatientAccessAuthorizationLookup> FindAsync(
        EntityId managerProfileId,
        EntityId targetProfileId,
        CancellationToken cancellationToken = default)
    {
        var targetExists = await dbContext.PatientProfiles
            .AsNoTracking()
            .AnyAsync(
                profile => profile.Id == targetProfileId,
                cancellationToken);
        if (!targetExists)
        {
            return new PatientAccessAuthorizationLookup(false, null);
        }

        var relationship = await dbContext.CareRelationships
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.ManagerProfileId == managerProfileId &&
                    candidate.SubjectProfileId == targetProfileId &&
                    candidate.Status == CareRelationshipStatus.Active,
                cancellationToken);

        return new PatientAccessAuthorizationLookup(true, relationship?.Id);
    }
}
