using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    public async Task<PatientAccessAuthorizationLookup> FindForPatientUpdateAsync(
        EntityId managerProfileId,
        EntityId targetProfileId,
        CancellationToken cancellationToken = default)
    {
        var transaction = dbContext.Database.CurrentTransaction ??
            throw new InvalidOperationException(
                "Patient update authorization requires an active transaction.");
        var targetExists = await dbContext.PatientProfiles
            .AsNoTracking()
            .AnyAsync(
                profile => profile.Id == targetProfileId,
                cancellationToken);
        if (!targetExists)
        {
            return new PatientAccessAuthorizationLookup(false, null);
        }

        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "SELECT id FROM patients.care_relationships " +
            "WHERE manager_profile_id = @managerProfileId " +
            "AND subject_profile_id = @targetProfileId AND status = 'active' " +
            "FOR SHARE";

        var managerParameter = command.CreateParameter();
        managerParameter.ParameterName = "managerProfileId";
        managerParameter.Value = managerProfileId.Value;
        command.Parameters.Add(managerParameter);

        var targetParameter = command.CreateParameter();
        targetParameter.ParameterName = "targetProfileId";
        targetParameter.Value = targetProfileId.Value;
        command.Parameters.Add(targetParameter);

        var relationshipId = await command.ExecuteScalarAsync(cancellationToken);
        return new PatientAccessAuthorizationLookup(
            true,
            relationshipId is Guid value ? EntityId.From(value) : null);
    }
}
