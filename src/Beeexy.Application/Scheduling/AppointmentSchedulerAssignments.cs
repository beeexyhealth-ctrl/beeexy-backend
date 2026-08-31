using Beeexy.Domain.Common;

namespace Beeexy.Application.Scheduling;

public sealed record AppointmentSchedulerAssignment(
    EntityId AccountId,
    IReadOnlyCollection<EntityId> ClinicIds);

public sealed class AppointmentSchedulerAssignments
{
    private readonly IReadOnlyDictionary<EntityId, IReadOnlySet<EntityId>> assignments;

    private AppointmentSchedulerAssignments(
        IReadOnlyDictionary<EntityId, IReadOnlySet<EntityId>> assignments)
    {
        this.assignments = assignments;
    }

    public static AppointmentSchedulerAssignments Empty { get; } =
        new(new Dictionary<EntityId, IReadOnlySet<EntityId>>());

    public static AppointmentSchedulerAssignments Create(
        IEnumerable<AppointmentSchedulerAssignment> configuredAssignments)
    {
        ArgumentNullException.ThrowIfNull(configuredAssignments);
        var result = new Dictionary<EntityId, IReadOnlySet<EntityId>>();
        foreach (var assignment in configuredAssignments)
        {
            ArgumentNullException.ThrowIfNull(assignment);
            if (assignment.AccountId.Value == Guid.Empty ||
                assignment.ClinicIds is null ||
                assignment.ClinicIds.Count == 0 ||
                assignment.ClinicIds.Any(clinicId => clinicId.Value == Guid.Empty) ||
                !result.TryAdd(
                    assignment.AccountId,
                    assignment.ClinicIds.ToHashSet()))
            {
                throw new ArgumentException(
                    "Scheduler assignments require one unique account and at least one valid clinic.",
                    nameof(configuredAssignments));
            }
        }

        return new AppointmentSchedulerAssignments(result);
    }

    public bool HasAppointmentSchedulerPermission(
        EntityId accountId,
        EntityId clinicId) =>
        accountId.Value != Guid.Empty &&
        clinicId.Value != Guid.Empty &&
        assignments.TryGetValue(accountId, out var clinics) &&
        clinics.Contains(clinicId);
}
