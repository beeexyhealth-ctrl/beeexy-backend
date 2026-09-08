using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Application.Care;
using Beeexy.Application.Common;
using Beeexy.Domain.Common;

namespace Beeexy.Infrastructure.Care;

public sealed class SymptomDiaryHistoryCursorCodec : ISymptomDiaryHistoryCursorCodec
{
    private const int CursorVersion = 1;
    private const int SignatureLength = 32;
    private const int MaximumEncodedLength = 2048;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null
    };

    private readonly byte[] signingKey;

    public SymptomDiaryHistoryCursorCodec(string applicationSigningKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationSigningKey);
        signingKey = SHA256.HashData(Encoding.UTF8.GetBytes(
            "beeexy:symptom-diary-history-cursor:v1:" + applicationSigningKey));
    }

    public string Encode(SymptomCheckInPageCursor cursor)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(
            CursorVersion,
            cursor.EpisodeId.Value,
            cursor.PageSize,
            cursor.CreatedAt.ToUniversalTime(),
            cursor.CheckInId.Value), SerializerOptions);
        var signature = HMACSHA256.HashData(signingKey, payload);
        var protectedPayload = new byte[payload.Length + signature.Length];
        payload.CopyTo(protectedPayload, 0);
        signature.CopyTo(protectedPayload, payload.Length);
        return Base64UrlEncode(protectedPayload);
    }

    public SymptomCheckInPageCursor Decode(
        string encoded,
        EntityId expectedEpisodeId,
        int expectedPageSize)
    {
        if (string.IsNullOrWhiteSpace(encoded) ||
            encoded.Length > MaximumEncodedLength ||
            encoded.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw SymptomDiaryHistoryCursorErrors.Invalid();
        }

        try
        {
            var protectedPayload = Base64UrlDecode(encoded);
            if (protectedPayload.Length <= SignatureLength ||
                Base64UrlEncode(protectedPayload) != encoded)
            {
                throw SymptomDiaryHistoryCursorErrors.Invalid();
            }

            var payload = protectedPayload.AsSpan(0, protectedPayload.Length - SignatureLength);
            var signature = protectedPayload.AsSpan(protectedPayload.Length - SignatureLength);
            var expectedSignature = HMACSHA256.HashData(signingKey, payload);
            if (!CryptographicOperations.FixedTimeEquals(signature, expectedSignature))
            {
                throw SymptomDiaryHistoryCursorErrors.Invalid();
            }

            var value = JsonSerializer.Deserialize<CursorPayload>(payload, SerializerOptions);
            if (value is null ||
                value.Version != CursorVersion ||
                value.EpisodeId == Guid.Empty ||
                value.CheckInId == Guid.Empty ||
                value.PageSize is < 1 or > ListSymptomCheckIns.MaximumPageSize ||
                value.CreatedAt == default ||
                value.CreatedAt.Offset != TimeSpan.Zero ||
                value.EpisodeId != expectedEpisodeId.Value ||
                value.PageSize != expectedPageSize)
            {
                throw SymptomDiaryHistoryCursorErrors.Invalid();
            }

            return new SymptomCheckInPageCursor(
                expectedEpisodeId,
                expectedPageSize,
                value.CreatedAt,
                EntityId.From(value.CheckInId));
        }
        catch (RequestValidationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            throw SymptomDiaryHistoryCursorErrors.Invalid();
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string encoded)
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

    private sealed record CursorPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("episodeId")] Guid EpisodeId,
        [property: JsonPropertyName("pageSize")] int PageSize,
        [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("checkInId")] Guid CheckInId);
}
