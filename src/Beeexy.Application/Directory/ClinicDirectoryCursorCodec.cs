using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Common;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Directory;

internal static class DirectoryCursorCodec
{
    private const int CursorVersion = 1;
    private const int MaximumEncodedLength = 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public static string EncodeClinic(ClinicDirectoryPageCursor cursor) =>
        EncodePayload(new CursorPayload(
            CursorVersion,
            cursor.ClinicId.Value,
            cursor.Filter.Code,
            cursor.Filter.Locality,
            cursor.Filter.AdministrativeArea,
            cursor.Filter.Country));

    public static ClinicDirectoryPageCursor DecodeClinic(
        string encoded,
        ClinicDirectoryFilter expectedFilter)
    {
        if (string.IsNullOrWhiteSpace(encoded) ||
            encoded.Length > MaximumEncodedLength ||
            encoded.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw CreateInvalidClinicCursorException();
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
                throw CreateInvalidClinicCursorException();
            }

            var payloadFilter = new ClinicDirectoryFilter(
                payload.Code,
                payload.Locality,
                payload.AdministrativeArea,
                payload.Country);
            if (payloadFilter != expectedFilter)
            {
                throw CreateInvalidClinicCursorException();
            }

            return new ClinicDirectoryPageCursor(
                expectedFilter,
                EntityId.From(payload.ClinicId));
        }
        catch (RequestValidationException)
        {
            throw CreateInvalidClinicCursorException();
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            throw CreateInvalidClinicCursorException();
        }
    }

    public static string EncodeDoctor(DoctorDirectoryPageCursor cursor) =>
        EncodePayload(new DoctorCursorPayload(
            CursorVersion,
            cursor.DoctorId.Value,
            cursor.Filter.SpecialtyCode,
            cursor.Filter.LanguageCode,
            cursor.Filter.Locality,
            cursor.Filter.AdministrativeArea,
            cursor.Filter.Country,
            cursor.Filter.InsurancePlanCode));

    public static DoctorDirectoryPageCursor DecodeDoctor(
        string encoded,
        DoctorDirectoryFilter expectedFilter)
    {
        EnsureEncodedCursorIsValid(encoded, CreateInvalidDoctorCursorException);

        try
        {
            var bytes = DecodeBase64Url(encoded);
            var payload = JsonSerializer.Deserialize<DoctorCursorPayload>(bytes, SerializerOptions);
            if (payload is null ||
                payload.Version != CursorVersion ||
                payload.DoctorId == Guid.Empty ||
                EncodePayload(payload) != encoded)
            {
                throw CreateInvalidDoctorCursorException();
            }

            var payloadFilter = new DoctorDirectoryFilter(
                payload.SpecialtyCode,
                payload.LanguageCode,
                payload.Locality,
                payload.AdministrativeArea,
                payload.Country,
                payload.InsurancePlanCode);
            if (payloadFilter != expectedFilter)
            {
                throw CreateInvalidDoctorCursorException();
            }

            return new DoctorDirectoryPageCursor(
                expectedFilter,
                EntityId.From(payload.DoctorId));
        }
        catch (RequestValidationException)
        {
            throw CreateInvalidDoctorCursorException();
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            throw CreateInvalidDoctorCursorException();
        }
    }

    internal static RequestValidationException CreateInvalidClinicCursorException() =>
        new(
            "clinic_directory.cursor_invalid",
            "The clinic directory cursor is invalid for this request.");

    internal static RequestValidationException CreateInvalidDoctorCursorException() =>
        new(
            "doctor_directory.cursor_invalid",
            "The doctor directory cursor is invalid for this request.");

    private static void EnsureEncodedCursorIsValid(
        string encoded,
        Func<RequestValidationException> createException)
    {
        if (string.IsNullOrWhiteSpace(encoded) ||
            encoded.Length > MaximumEncodedLength ||
            encoded.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw createException();
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

    private static string EncodePayload<TPayload>(TPayload payload)
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

    private sealed record DoctorCursorPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("doctorId")] Guid DoctorId,
        [property: JsonPropertyName("specialtyCode")] string? SpecialtyCode,
        [property: JsonPropertyName("languageCode")] string? LanguageCode,
        [property: JsonPropertyName("locality")] string? Locality,
        [property: JsonPropertyName("administrativeArea")] string? AdministrativeArea,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("insurancePlanCode")] string? InsurancePlanCode);
}
