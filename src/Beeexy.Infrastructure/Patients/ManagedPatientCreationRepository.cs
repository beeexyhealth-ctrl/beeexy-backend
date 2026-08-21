using Beeexy.Application.Patients;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Beeexy.Infrastructure.Patients;

public sealed class ManagedPatientCreationRepository(BeeexyDbContext dbContext)
    : IManagedPatientCreationRepository
{
    private static readonly HashSet<string> CreationConflictConstraints =
    [
        "pk_patient_profiles",
        "ux_patient_profiles_beeexy_id",
        "pk_care_relationships",
        "ux_care_relationships_active_manager_subject"
    ];

    public void Add(PatientProfile subject, CareRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(relationship);
        dbContext.AddRange(subject, relationship);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: not null
            } postgresException &&
            CreationConflictConstraints.Contains(postgresException.ConstraintName))
        {
            throw new ManagedPatientCreationConflictException();
        }
    }
}
