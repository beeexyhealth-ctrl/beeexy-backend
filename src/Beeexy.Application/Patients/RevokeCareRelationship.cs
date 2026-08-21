using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public sealed class RevokeCareRelationship(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    ICareRelationshipRevocationRepository repository,
    IIdentityVerificationTransaction transaction,
    ICareRelationshipAuditLogger auditLogger)
{
    public async Task ExecuteAsync(
        EntityId relationshipId,
        CancellationToken cancellationToken = default)
    {
        await transaction.BeginAsync(cancellationToken);
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var relationship = await repository.FindForUpdateAsync(
            relationshipId,
            current.PrimaryProfile.Id,
            cancellationToken);

        if (relationship is null)
        {
            throw new CareRelationshipNotFoundException();
        }

        if (relationship.Status == CareRelationshipStatus.Revoked)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var revokedAt = clock.UtcNow;
        relationship.Revoke(current.Account.Id, revokedAt);
        await transaction.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        auditLogger.RevocationSucceeded(
            current.Account.Id,
            current.PrimaryProfile.Id,
            relationship.SubjectProfileId,
            relationship.Id,
            relationship.RelationshipType,
            revokedAt);
    }
}

public sealed class CareRelationshipNotFoundException : Exception
{
    public CareRelationshipNotFoundException()
        : base("The care relationship was not found.")
    {
    }
}
