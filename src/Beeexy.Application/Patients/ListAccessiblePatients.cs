using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;

namespace Beeexy.Application.Patients;

public sealed class ListAccessiblePatients(
    CurrentAccountProfileResolver currentAccountResolver,
    IMyCircleReadRepository repository,
    IMyCircleAuditLogger auditLogger)
{
    public async Task<ListAccessiblePatientsResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var managedPatients = await repository.ListActiveManagedPatientsAsync(
            current.PrimaryProfile.Id,
            cancellationToken);

        var patients = new List<AccessiblePatientSummary>
        {
            new(
                current.PrimaryProfile.Id,
                current.PrimaryProfile.BeeexyId.Value,
                current.PrimaryProfile.FirstName?.Value,
                current.PrimaryProfile.LastName?.Value,
                PatientAccessType.Primary,
                null)
        };
        var seenProfileIds = new HashSet<EntityId> { current.PrimaryProfile.Id };

        foreach (var managedPatient in managedPatients
                     .Where(value => value.RelationshipStatus == CareRelationshipStatus.Active)
                     .OrderBy(value => value.RelationshipCreatedAt)
                     .ThenBy(value => value.RelationshipId.Value))
        {
            if (!seenProfileIds.Add(managedPatient.ProfileId))
            {
                auditLogger.DuplicateAccessiblePatientDetected(
                    current.Account.Id,
                    current.PrimaryProfile.Id,
                    managedPatient.ProfileId);
                continue;
            }

            patients.Add(new AccessiblePatientSummary(
                managedPatient.ProfileId,
                managedPatient.BeeexyId,
                managedPatient.FirstName,
                managedPatient.LastName,
                PatientAccessType.Managed,
                new AccessiblePatientRelationshipSummary(
                    managedPatient.RelationshipId,
                    managedPatient.RelationshipType)));
        }

        return new ListAccessiblePatientsResult(patients);
    }
}

public enum PatientAccessType
{
    Primary = 0,
    Managed = 1
}

public sealed record ListAccessiblePatientsResult(
    IReadOnlyList<AccessiblePatientSummary> Patients);

public sealed record AccessiblePatientSummary(
    EntityId ProfileId,
    string BeeexyId,
    string? FirstName,
    string? LastName,
    PatientAccessType AccessType,
    AccessiblePatientRelationshipSummary? Relationship);

public sealed record AccessiblePatientRelationshipSummary(
    EntityId RelationshipId,
    CareRelationshipType RelationshipType);
