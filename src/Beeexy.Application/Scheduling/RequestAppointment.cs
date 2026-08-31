using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Application.Scheduling;

public sealed record RequestAppointmentCommand(
    EntityId PatientProfileId,
    EntityId AvailabilitySlotId,
    AppointmentModality Modality,
    string? Reason,
    EntityId IdempotencyKey);

public sealed record RequestedAppointment(
    EntityId AppointmentId,
    EntityId PatientProfileId,
    EntityId AvailabilitySlotId,
    EntityId DoctorId,
    EntityId ClinicId,
    EntityId ClinicLocationId,
    AppointmentStatus Status,
    AppointmentModality Modality,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string ClinicTimeZone,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record RequestAppointmentResult(
    RequestedAppointment Appointment,
    bool NewlyCreated);

public sealed record AppointmentRequestState(
    Appointment Appointment,
    AvailabilitySlot Slot);

public sealed record AppointmentSlotRequestState(
    AvailabilitySlot Slot,
    bool HasEligibleDirectoryRelationships);

public sealed record AppointmentRequestSaveResult(
    AppointmentRequestState State,
    bool NewlyCreated);

public interface IAppointmentRequestTransaction : IAsyncDisposable
{
    Task BeginAsync(
        EntityId requestingAccountId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AppointmentRequestState?> FindExistingAsync(
        EntityId requestingAccountId,
        EntityId idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AppointmentSlotRequestState?> FindSlotAsync(
        EntityId slotId,
        CancellationToken cancellationToken = default);

    void Add(Appointment appointment);

    Task<AppointmentRequestSaveResult> SaveAsync(
        Appointment appointment,
        AvailabilitySlot slot,
        CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

public sealed class AppointmentNotFoundException : Exception
{
    public AppointmentNotFoundException()
        : base("The appointment request target could not be found.")
    {
    }
}

public sealed class AppointmentSlotReservationConflictException : Exception
{
    public AppointmentSlotReservationConflictException()
        : base("The selected availability slot is already reserved.")
    {
    }
}

public sealed class AppointmentIdempotencyConflictException : Exception
{
    public AppointmentIdempotencyConflictException()
        : base("The idempotency key was already used for a different appointment request.")
    {
    }
}

public static class AppointmentRequestFingerprintCalculator
{
    public static AppointmentRequestFingerprint Calculate(
        EntityId patientProfileId,
        EntityId availabilitySlotId,
        AppointmentModality modality,
        AppointmentReason? reason)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("patientId", patientProfileId.Value.ToString("D"));
            writer.WriteString("slotId", availabilitySlotId.Value.ToString("D"));
            writer.WriteString("modality", ToCanonicalCode(modality));
            if (reason is null)
            {
                writer.WriteNull("reason");
            }
            else
            {
                writer.WriteString("reason", reason.Value);
            }

            writer.WriteEndObject();
        }

        return AppointmentRequestFingerprint.Create(
            Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant());
    }

    private static string ToCanonicalCode(AppointmentModality modality) => modality switch
    {
        AppointmentModality.InPerson => "in_person",
        AppointmentModality.Virtual => "virtual",
        _ => throw new RequestValidationException(
            "scheduling.modality_invalid",
            "The appointment modality is invalid.")
    };
}

public sealed class RequestAppointment(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    AuthorizePatientAccess authorizePatientAccess,
    IAppointmentRequestTransaction transaction)
{
    public async Task<RequestAppointmentResult> ExecuteAsync(
        RequestAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateIdentifiers(command);
        ValidateModality(command.Modality);
        var reason = CreateReason(command.Reason);
        var fingerprint = AppointmentRequestFingerprintCalculator.Calculate(
            command.PatientProfileId,
            command.AvailabilitySlotId,
            command.Modality,
            reason);

        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var authorization = await authorizePatientAccess.ExecuteAsync(
            command.PatientProfileId,
            current,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new AppointmentNotFoundException();
        }

        await transaction.BeginAsync(
            current.Account.Id,
            command.IdempotencyKey,
            cancellationToken);

        if (authorization.Reason == PatientAccessReason.Managed)
        {
            authorization = await authorizePatientAccess.ExecuteForPatientUpdateAsync(
                command.PatientProfileId,
                current,
                cancellationToken);
            if (!authorization.IsAuthorized)
            {
                throw new AppointmentNotFoundException();
            }
        }

        var existing = await transaction.FindExistingAsync(
            current.Account.Id,
            command.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            EnsureFingerprintMatches(existing.Appointment, fingerprint);
            await transaction.CommitAsync(cancellationToken);
            return new RequestAppointmentResult(ToResult(existing), NewlyCreated: false);
        }

        var slotState = await transaction.FindSlotAsync(
            command.AvailabilitySlotId,
            cancellationToken);
        if (slotState is null || !slotState.HasEligibleDirectoryRelationships)
        {
            throw new AppointmentNotFoundException();
        }

        var slot = slotState.Slot;
        if (!slot.IsPublished)
        {
            throw new RequestValidationException(
                "scheduling.slot_unbookable",
                "The selected availability slot cannot be requested.");
        }

        var requestedAt = NormalizePostgreSqlInstant(clock.UtcNow);
        if (slot.StartsAt <= requestedAt)
        {
            throw new RequestValidationException(
                "scheduling.slot_expired",
                "The selected availability slot is no longer in the future.");
        }

        if (slot.Modality != command.Modality)
        {
            throw new RequestValidationException(
                "scheduling.modality_mismatch",
                "The appointment modality does not match the selected slot.");
        }

        var appointment = Appointment.Create(
            command.PatientProfileId,
            slot,
            current.Account.Id,
            command.Modality,
            reason,
            command.IdempotencyKey,
            fingerprint,
            requestedAt);
        transaction.Add(appointment);
        var saveResult = await transaction.SaveAsync(
            appointment,
            slot,
            cancellationToken);
        EnsureFingerprintMatches(saveResult.State.Appointment, fingerprint);
        await transaction.CommitAsync(cancellationToken);

        return new RequestAppointmentResult(
            ToResult(saveResult.State),
            saveResult.NewlyCreated);
    }

    private static RequestedAppointment ToResult(AppointmentRequestState state) => new(
        state.Appointment.Id,
        state.Appointment.PatientProfileId,
        state.Appointment.AvailabilitySlotId,
        state.Slot.DoctorId,
        state.Slot.ClinicId,
        state.Slot.ClinicLocationId,
        state.Appointment.Status,
        state.Appointment.Modality,
        state.Slot.StartsAt,
        state.Slot.EndsAt,
        state.Slot.ClinicTimeZone.Value,
        state.Appointment.Reason?.Value,
        state.Appointment.CreatedAt);

    private static void EnsureFingerprintMatches(
        Appointment existing,
        AppointmentRequestFingerprint fingerprint)
    {
        if (existing.RequestFingerprint != fingerprint)
        {
            throw new AppointmentIdempotencyConflictException();
        }
    }

    private static AppointmentReason? CreateReason(string? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return AppointmentReason.Create(value);
        }
        catch (ArgumentException)
        {
            throw new RequestValidationException(
                "scheduling.reason_invalid",
                $"The appointment reason must contain text and cannot exceed {AppointmentReason.MaximumLength} characters.");
        }
    }

    private static void ValidateIdentifiers(RequestAppointmentCommand command)
    {
        if (command.PatientProfileId.Value == Guid.Empty ||
            command.AvailabilitySlotId.Value == Guid.Empty ||
            command.IdempotencyKey.Value == Guid.Empty)
        {
            throw new RequestValidationException(
                "scheduling.identifiers_required",
                "Patient, slot, and idempotency identifiers are required.");
        }
    }

    private static void ValidateModality(AppointmentModality modality)
    {
        if (!Enum.IsDefined(modality))
        {
            throw new RequestValidationException(
                "scheduling.modality_invalid",
                "The appointment modality is invalid.");
        }
    }

    private static DateTimeOffset NormalizePostgreSqlInstant(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}
