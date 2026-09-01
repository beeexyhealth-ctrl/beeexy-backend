using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Application.Scheduling;

public sealed record OperationalAppointmentSummary(
    EntityId AppointmentId,
    EntityId ClinicId,
    string Doctor,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string ClinicTimeZone,
    AppointmentModality Modality,
    AppointmentStatus Status,
    DateTimeOffset CreatedAt);

public interface IAppointmentOperationsReadRepository
{
    Task<IReadOnlyList<OperationalAppointmentSummary>> ListRequestedAsync(
        EntityId clinicId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<OperationalAppointmentSummary?> GetAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default);
}

public sealed class ListRequestedAppointmentsForOperations(
    IAppointmentOperationsReadRepository repository)
{
    public const int DefaultLimit = 50;
    public const int MaximumLimit = 200;

    public Task<IReadOnlyList<OperationalAppointmentSummary>> ExecuteAsync(
        EntityId clinicId,
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (clinicId.Value == Guid.Empty)
        {
            throw new ArgumentException("A clinic identifier is required.", nameof(clinicId));
        }

        if (limit is < 1 or > MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"The limit must be between 1 and {MaximumLimit}.");
        }

        return repository.ListRequestedAsync(clinicId, limit, cancellationToken);
    }
}

public sealed class GetAppointmentForOperations(
    IAppointmentOperationsReadRepository repository)
{
    public async Task<OperationalAppointmentSummary> ExecuteAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default)
    {
        if (appointmentId.Value == Guid.Empty)
        {
            throw new AppointmentNotFoundException();
        }

        return await repository.GetAsync(appointmentId, cancellationToken)
            ?? throw new AppointmentNotFoundException();
    }
}
