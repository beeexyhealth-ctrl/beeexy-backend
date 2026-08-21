using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Beeexy.Infrastructure.Patients;

public sealed class CareRelationshipRevocationRepository(BeeexyDbContext dbContext)
    : ICareRelationshipRevocationRepository
{
    public async Task<CareRelationship?> FindForUpdateAsync(
        EntityId relationshipId,
        EntityId managerProfileId,
        CancellationToken cancellationToken = default)
    {
        var transaction = dbContext.Database.CurrentTransaction ??
            throw new InvalidOperationException(
                "Care relationship revocation requires an active transaction.");
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "SELECT id FROM patients.care_relationships " +
            "WHERE id = @relationshipId AND manager_profile_id = @managerProfileId " +
            "FOR UPDATE";

        var relationshipParameter = command.CreateParameter();
        relationshipParameter.ParameterName = "relationshipId";
        relationshipParameter.Value = relationshipId.Value;
        command.Parameters.Add(relationshipParameter);

        var managerParameter = command.CreateParameter();
        managerParameter.ParameterName = "managerProfileId";
        managerParameter.Value = managerProfileId.Value;
        command.Parameters.Add(managerParameter);

        var lockedRelationshipId = await command.ExecuteScalarAsync(cancellationToken);
        if (lockedRelationshipId is null)
        {
            return null;
        }

        return await dbContext.CareRelationships.SingleAsync(
            relationship => relationship.Id == relationshipId,
            cancellationToken);
    }
}
