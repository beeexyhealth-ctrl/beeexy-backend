using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Infrastructure.Patients;

public sealed class MyCircleReadRepository(BeeexyDbContext dbContext)
    : IMyCircleReadRepository
{
    public async Task<IReadOnlyList<ManagedPatientAccessRecord>>
        ListActiveManagedPatientsAsync(
            EntityId managerProfileId,
            CancellationToken cancellationToken = default)
    {
        var rows = await (
                from relationship in dbContext.CareRelationships.AsNoTracking()
                join subject in dbContext.PatientProfiles.AsNoTracking()
                    on relationship.SubjectProfileId equals subject.Id
                where relationship.ManagerProfileId == managerProfileId &&
                      relationship.Status == CareRelationshipStatus.Active
                orderby relationship.CreatedAt, relationship.Id
                select new
                {
                    Subject = subject,
                    Relationship = relationship
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new ManagedPatientAccessRecord(
                row.Subject.Id,
                row.Subject.BeeexyId.Value,
                row.Subject.FirstName?.Value,
                row.Subject.LastName?.Value,
                row.Relationship.Id,
                row.Relationship.RelationshipType,
                row.Relationship.Status,
                row.Relationship.CreatedAt))
            .ToArray();
    }

    public async Task<IReadOnlyList<CareRelationshipListRecord>> ListRelationshipsAsync(
        EntityId managerProfileId,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
                from relationship in dbContext.CareRelationships.AsNoTracking()
                join subject in dbContext.PatientProfiles.AsNoTracking()
                    on relationship.SubjectProfileId equals subject.Id
                where relationship.ManagerProfileId == managerProfileId
                orderby relationship.CreatedAt, relationship.Id
                select new
                {
                    Subject = subject,
                    Relationship = relationship
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new CareRelationshipListRecord(
                row.Relationship.Id,
                row.Subject.Id,
                row.Subject.BeeexyId.Value,
                row.Subject.FirstName?.Value,
                row.Subject.LastName?.Value,
                row.Relationship.RelationshipType,
                row.Relationship.Status,
                row.Relationship.Attestation.Version,
                row.Relationship.Attestation.AttestedAt,
                row.Relationship.CreatedAt,
                row.Relationship.RevokedAt))
            .ToArray();
    }
}
