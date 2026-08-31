using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Application.Scheduling;

public sealed record AppointmentListFilter(
    EntityId? PatientProfileId,
    AppointmentStatus? Status,
    DateTimeOffset? From,
    DateTimeOffset? To);

public sealed record AppointmentPageCursor(
    AppointmentListFilter Filter,
    DateTimeOffset ScheduledStartAt,
    EntityId AppointmentId);

public sealed record AppointmentSummary(
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
    DateTimeOffset CreatedAt);

public sealed record AppointmentStatusHistoryItem(
    long Sequence,
    AppointmentStatus? PreviousStatus,
    AppointmentStatus NewStatus,
    AppointmentActorType ActorType,
    AppointmentStatusAction Action,
    DateTimeOffset OccurredAt);

public sealed record AppointmentRescheduleHistoryItem(
    EntityId PreviousSlotId,
    EntityId NewSlotId,
    DateTimeOffset OccurredAt);

public sealed record AppointmentDetail(
    AppointmentSummary Appointment,
    string? Reason,
    IReadOnlyList<AppointmentStatusHistoryItem> StatusHistory,
    IReadOnlyList<AppointmentRescheduleHistoryItem> RescheduleHistory);

public interface IAppointmentReadRepository
{
    Task<bool> CursorExistsAsync(
        EntityId accessiblePrimaryProfileId,
        AppointmentPageCursor cursor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppointmentSummary>> ListAsync(
        EntityId accessiblePrimaryProfileId,
        AppointmentListFilter filter,
        AppointmentPageCursor? after,
        int take,
        CancellationToken cancellationToken = default);

    Task<EntityId?> FindPatientProfileIdAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default);

    Task<AppointmentDetail?> GetAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default);
}

public sealed record ListAppointmentsQuery(
    EntityId? PatientProfileId = null,
    string? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Cursor = null,
    int? PageSize = null);

public sealed record ListAppointmentsResult(
    IReadOnlyList<AppointmentSummary> Items,
    string? NextCursor);

public sealed class ListAppointments(
    CurrentAccountProfileResolver currentAccountResolver,
    AuthorizePatientAccess authorizePatientAccess,
    IAppointmentReadRepository repository)
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;

    public async Task<ListAppointmentsResult> ExecuteAsync(
        ListAppointmentsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var status = AppointmentStatuses.ParseOptionalFilter(query.Status);
        var from = query.From?.ToUniversalTime();
        var to = query.To?.ToUniversalTime();
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
        {
            throw new RequestValidationException(
                "scheduling.appointment_range_invalid",
                "The appointment time range must have an end after its start.");
        }

        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new RequestValidationException(
                "scheduling.appointment_page_size_invalid",
                $"Page size must be between 1 and {MaximumPageSize}.");
        }

        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        if (query.PatientProfileId is { } patientProfileId)
        {
            if (patientProfileId.Value == Guid.Empty)
            {
                throw new AppointmentNotFoundException();
            }

            var authorization = await authorizePatientAccess.ExecuteAsync(
                patientProfileId,
                current,
                cancellationToken);
            if (!authorization.IsAuthorized)
            {
                throw new AppointmentNotFoundException();
            }
        }

        var filter = new AppointmentListFilter(
            query.PatientProfileId,
            status,
            from,
            to);
        var cursor = query.Cursor is null
            ? null
            : AppointmentCursorCodec.Decode(query.Cursor, filter);
        if (cursor is not null &&
            !await repository.CursorExistsAsync(
                current.PrimaryProfile.Id,
                cursor,
                cancellationToken))
        {
            throw AppointmentCursorCodec.CreateInvalidCursorException();
        }

        var page = await repository.ListAsync(
            current.PrimaryProfile.Id,
            filter,
            cursor,
            pageSize + 1,
            cancellationToken);
        var hasMore = page.Count > pageSize;
        var items = page.Take(pageSize).ToArray();
        var nextCursor = hasMore
            ? AppointmentCursorCodec.Encode(new AppointmentPageCursor(
                filter,
                items[^1].StartsAt,
                items[^1].AppointmentId))
            : null;

        return new ListAppointmentsResult(items, nextCursor);
    }
}

public sealed class GetAppointment(
    CurrentAccountProfileResolver currentAccountResolver,
    AuthorizePatientAccess authorizePatientAccess,
    IAppointmentReadRepository repository)
{
    public async Task<AppointmentDetail> ExecuteAsync(
        EntityId appointmentId,
        CancellationToken cancellationToken = default)
    {
        if (appointmentId.Value == Guid.Empty)
        {
            throw new AppointmentNotFoundException();
        }

        var patientProfileId = await repository.FindPatientProfileIdAsync(
            appointmentId,
            cancellationToken) ?? throw new AppointmentNotFoundException();
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var authorization = await authorizePatientAccess.ExecuteAsync(
            patientProfileId,
            current,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new AppointmentNotFoundException();
        }

        return await repository.GetAsync(appointmentId, cancellationToken)
            ?? throw new AppointmentNotFoundException();
    }
}

public static class AppointmentStatuses
{
    public static AppointmentStatus? ParseOptionalFilter(string? value) =>
        value is null ? null : ParseFilter(value);

    internal static AppointmentStatus ParseFilter(string value) => value switch
    {
        "Requested" => AppointmentStatus.Requested,
        "Confirmed" => AppointmentStatus.Confirmed,
        "Cancelled" => AppointmentStatus.Cancelled,
        "Completed" => AppointmentStatus.Completed,
        "NoShow" => AppointmentStatus.NoShow,
        "Rejected" => AppointmentStatus.Rejected,
        _ => throw new RequestValidationException(
            "scheduling.appointment_status_invalid",
            "The appointment status is not supported.")
    };

    public static string ToApiValue(AppointmentStatus value) => value switch
    {
        AppointmentStatus.Requested => "Requested",
        AppointmentStatus.Confirmed => "Confirmed",
        AppointmentStatus.Cancelled => "Cancelled",
        AppointmentStatus.Completed => "Completed",
        AppointmentStatus.NoShow => "NoShow",
        AppointmentStatus.Rejected => "Rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

internal static class AppointmentCursorCodec
{
    private const int CursorVersion = 1;
    private const int MaximumEncodedLength = 2048;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public static string Encode(AppointmentPageCursor cursor) => EncodePayload(
        new CursorPayload(
            CursorVersion,
            cursor.Filter.PatientProfileId?.Value,
            cursor.Filter.Status is null
                ? null
                : AppointmentStatuses.ToApiValue(cursor.Filter.Status.Value),
            cursor.Filter.From?.ToUniversalTime(),
            cursor.Filter.To?.ToUniversalTime(),
            cursor.ScheduledStartAt.ToUniversalTime(),
            cursor.AppointmentId.Value));

    public static AppointmentPageCursor Decode(
        string encoded,
        AppointmentListFilter expectedFilter)
    {
        if (string.IsNullOrWhiteSpace(encoded) ||
            encoded.Length > MaximumEncodedLength ||
            encoded.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw CreateInvalidCursorException();
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CursorPayload>(
                DecodeBase64Url(encoded),
                SerializerOptions);
            if (payload is null ||
                payload.Version != CursorVersion ||
                payload.AppointmentId == Guid.Empty ||
                !IsUtcInstant(payload.ScheduledStartAt) ||
                (payload.From.HasValue && !IsUtcInstant(payload.From.Value)) ||
                (payload.To.HasValue && !IsUtcInstant(payload.To.Value)) ||
                EncodePayload(payload) != encoded)
            {
                throw CreateInvalidCursorException();
            }

            AppointmentStatus? status = payload.Status is null
                ? null
                : AppointmentStatuses.ParseFilter(payload.Status);
            var filter = new AppointmentListFilter(
                payload.PatientId.HasValue
                    ? EntityId.From(payload.PatientId.Value)
                    : null,
                status,
                payload.From,
                payload.To);
            if (filter != expectedFilter)
            {
                throw CreateInvalidCursorException();
            }

            return new AppointmentPageCursor(
                filter,
                payload.ScheduledStartAt,
                EntityId.From(payload.AppointmentId));
        }
        catch (RequestValidationException)
        {
            throw CreateInvalidCursorException();
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            throw CreateInvalidCursorException();
        }
    }

    internal static RequestValidationException CreateInvalidCursorException() => new(
        "scheduling.appointment_cursor_invalid",
        "The appointment cursor is invalid for this request.");

    private static bool IsUtcInstant(DateTimeOffset value) =>
        value != default && value.Offset == TimeSpan.Zero;

    private static byte[] DecodeBase64Url(string encoded)
    {
        var base64 = encoded.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            0 => base64,
            2 => base64 + "==",
            3 => base64 + "=",
            _ => throw new FormatException("Invalid Base64URL length.")
        };
        return Convert.FromBase64String(base64);
    }

    private static string EncodePayload(CursorPayload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record CursorPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("patientId")] Guid? PatientId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("from")] DateTimeOffset? From,
        [property: JsonPropertyName("to")] DateTimeOffset? To,
        [property: JsonPropertyName("startsAt")] DateTimeOffset ScheduledStartAt,
        [property: JsonPropertyName("appointmentId")] Guid AppointmentId);
}
