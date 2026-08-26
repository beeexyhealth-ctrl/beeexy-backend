using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Beeexy.Api.PrivateAccess;

internal sealed class PrivateAccessSessionTokenService(PrivateAccessSettings settings)
{
    private const byte Version = 1;
    private const int PayloadLength = 1 + sizeof(long) + 16;
    private const int SignatureLength = 32;

    public PrivateAccessSession Issue(DateTimeOffset now)
    {
        EnsureEnabled();
        var expiresAt = now.Add(settings.SessionLifetime);
        Span<byte> payload = stackalloc byte[PayloadLength];
        payload[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(
            payload[1..(1 + sizeof(long))],
            expiresAt.ToUnixTimeSeconds());
        RandomNumberGenerator.Fill(payload[(1 + sizeof(long))..]);

        var signature = HMACSHA256.HashData(settings.SessionSigningKey!, payload);
        var tokenBytes = new byte[PayloadLength + SignatureLength];
        payload.CopyTo(tokenBytes);
        signature.CopyTo(tokenBytes, PayloadLength);
        return new PrivateAccessSession(WebEncoders.Base64UrlEncode(tokenBytes), expiresAt);
    }

    public bool TryValidate(
        string? token,
        DateTimeOffset now,
        out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        byte[] tokenBytes;
        try
        {
            tokenBytes = WebEncoders.Base64UrlDecode(token);
        }
        catch (FormatException)
        {
            return false;
        }

        if (tokenBytes.Length != PayloadLength + SignatureLength ||
            tokenBytes[0] != Version)
        {
            return false;
        }

        var payload = tokenBytes.AsSpan(0, PayloadLength);
        var suppliedSignature = tokenBytes.AsSpan(PayloadLength, SignatureLength);
        var expectedSignature = HMACSHA256.HashData(settings.SessionSigningKey!, payload);
        if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
        {
            return false;
        }

        try
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(
                BinaryPrimitives.ReadInt64BigEndian(payload[1..(1 + sizeof(long))]));
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return expiresAt > now;
    }

    private void EnsureEnabled()
    {
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("Private access is disabled.");
        }
    }
}

internal sealed record PrivateAccessSession(string Token, DateTimeOffset ExpiresAt);
