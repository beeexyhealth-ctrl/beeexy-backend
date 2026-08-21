using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public interface IManagedPatientCreationRepository
{
    void Add(PatientProfile subject, CareRelationship relationship);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
