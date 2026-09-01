using Beeexy.Domain.Scheduling;

namespace Beeexy.Infrastructure.Persistence;

internal static class SchedulingPersistence
{
    public const string ReservingAppointmentFilter =
        "status IN ('requested', 'confirmed')";

    public static string StoreModality(AppointmentModality modality) => modality switch
    {
        AppointmentModality.InPerson => "in_person",
        AppointmentModality.Virtual => "virtual",
        _ => throw new ArgumentOutOfRangeException(nameof(modality))
    };

    public static AppointmentModality LoadModality(string value) => value switch
    {
        "in_person" => AppointmentModality.InPerson,
        "virtual" => AppointmentModality.Virtual,
        _ => throw new InvalidOperationException("The stored appointment modality is unsupported.")
    };

    public static string StoreStatus(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Requested => "requested",
        AppointmentStatus.Confirmed => "confirmed",
        AppointmentStatus.Cancelled => "cancelled",
        AppointmentStatus.Completed => "completed",
        AppointmentStatus.NoShow => "no_show",
        AppointmentStatus.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static AppointmentStatus LoadStatus(string value) => value switch
    {
        "requested" => AppointmentStatus.Requested,
        "confirmed" => AppointmentStatus.Confirmed,
        "cancelled" => AppointmentStatus.Cancelled,
        "completed" => AppointmentStatus.Completed,
        "no_show" => AppointmentStatus.NoShow,
        "rejected" => AppointmentStatus.Rejected,
        _ => throw new InvalidOperationException("The stored appointment status is unsupported.")
    };

    public static string StoreActorType(AppointmentActorType actorType) => actorType switch
    {
        AppointmentActorType.PatientAuthority => "patient_authority",
        AppointmentActorType.AppointmentScheduler => "appointment_scheduler",
        AppointmentActorType.BeeexyOperations => "beeexy_operations",
        _ => throw new ArgumentOutOfRangeException(nameof(actorType))
    };

    public static AppointmentActorType LoadActorType(string value) => value switch
    {
        "patient_authority" => AppointmentActorType.PatientAuthority,
        "appointment_scheduler" => AppointmentActorType.AppointmentScheduler,
        "beeexy_operations" => AppointmentActorType.BeeexyOperations,
        _ => throw new InvalidOperationException("The stored appointment actor type is unsupported.")
    };

    public static string StoreAction(AppointmentStatusAction action) => action switch
    {
        AppointmentStatusAction.Creation => "creation",
        AppointmentStatusAction.Confirmation => "confirmation",
        AppointmentStatusAction.Rejection => "rejection",
        AppointmentStatusAction.Cancellation => "cancellation",
        AppointmentStatusAction.Completion => "completion",
        AppointmentStatusAction.NoShow => "no_show",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static AppointmentStatusAction LoadAction(string value) => value switch
    {
        "creation" => AppointmentStatusAction.Creation,
        "confirmation" => AppointmentStatusAction.Confirmation,
        "rejection" => AppointmentStatusAction.Rejection,
        "cancellation" => AppointmentStatusAction.Cancellation,
        "completion" => AppointmentStatusAction.Completion,
        "no_show" => AppointmentStatusAction.NoShow,
        _ => throw new InvalidOperationException("The stored appointment action is unsupported.")
    };
}
