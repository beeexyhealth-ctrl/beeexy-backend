using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public interface IPatientProfileUpdateRepository
{
    Task<PatientProfile?> FindAsync(
        EntityId profileId,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
