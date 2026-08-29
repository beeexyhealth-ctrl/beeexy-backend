using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Directory;

internal static class ClinicDirectoryCursorCodec
{
    private const int CursorVersion = 1;
    private const int MaximumEncodedLength = 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public static string Encode(ClinicDirectoryPageCursor cursor) =>
        EncodePayload(new CursorPayload(
            CursorVersion,
            cursor.ClinicId.Value,
            cursor.Filter.Code,
            cursor.Filter.Locality,
            cursor.Filter.AdministrativeArea,
            cursor.Filter.Country));

    public static ClinicDirectoryPageCursor Decode(
        string encoded,
        ClinicDirectoryFilter expectedFilter)
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
                payload.ClinicId == Guid.Empty ||
                EncodePayload(payload) != encoded)
            {
                throw CreateInvalidCursorException();
            }

            var payloadFilter = new ClinicDirectoryFilter(
                payload.Code,
                payload.Locality,
                payload.AdministrativeArea,
                payload.Country);
            if (payloadFilter != expectedFilter)
            {
                throw CreateInvalidCursorException();
            }

            return new ClinicDirectoryPageCursor(
                expectedFilter,
                EntityId.From(payload.ClinicId));
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

    internal static RequestValidationException CreateInvalidCursorException() =>
        new(
            "clinic_directory.cursor_invalid",
            "The clinic directory cursor is invalid for this request.");

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
        [property: JsonPropertyName("clinicId")] Guid ClinicId,
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("locality")] string? Locality,
        [property: JsonPropertyName("administrativeArea")] string? AdministrativeArea,
        [property: JsonPropertyName("country")] string? Country);
}
