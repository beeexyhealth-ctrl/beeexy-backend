using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;

namespace Beeexy.Application.History;

internal static class ClinicalHistoryCursorCodec
{
    private const int CursorVersion = 1;
    private const int MaximumEncodedLength = 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public static string Encode(ClinicalHistoryPageCursor cursor)
    {
        var payload = new CursorPayload(
            CursorVersion,
            cursor.PatientProfileId.Value,
            cursor.EventType is null
                ? null
                : ClinicalHistoryEventTypes.ToApiValue(cursor.EventType.Value),
            cursor.OccurredAt.ToUniversalTime(),
            cursor.EventId.Value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static ClinicalHistoryPageCursor Decode(
        string encoded,
        EntityId expectedPatientProfileId,
        ClinicalHistoryEventType? expectedEventType)
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
            var bytes = DecodeBase64Url(encoded);
            var payload = JsonSerializer.Deserialize<CursorPayload>(bytes, SerializerOptions);
            if (payload is null ||
                payload.Version != CursorVersion ||
                payload.PatientProfileId == Guid.Empty ||
                payload.EventId == Guid.Empty ||
                payload.OccurredAt == default ||
                payload.OccurredAt.Offset != TimeSpan.Zero ||
                EncodePayload(payload) != encoded)
            {
                throw CreateInvalidCursorException();
            }

            ClinicalHistoryEventType? eventType = payload.EventType is null
                ? null
                : ClinicalHistoryEventTypes.ParseFilter(payload.EventType);
            if (payload.PatientProfileId != expectedPatientProfileId.Value ||
                eventType != expectedEventType)
            {
                throw CreateInvalidCursorException();
            }

            return new ClinicalHistoryPageCursor(
                expectedPatientProfileId,
                eventType,
                payload.OccurredAt,
                EntityId.From(payload.EventId));
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

    internal static RequestValidationException CreateInvalidCursorException() =>
        new(
            "clinical_history.cursor_invalid",
            "The clinical history cursor is invalid for this request.");

    private sealed record CursorPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("patientId")] Guid PatientProfileId,
        [property: JsonPropertyName("eventType")] string? EventType,
        [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
        [property: JsonPropertyName("eventId")] Guid EventId);
}
