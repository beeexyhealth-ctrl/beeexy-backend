using System.Text.Json;
using Beeexy.Domain.Common;

namespace Beeexy.Domain.Ai;

internal static class AiGuard
{
    public static EntityId IdOrNew(EntityId? value, string parameterName)
    {
        var resolved = value ?? EntityId.New();
        EnsureId(resolved, parameterName);
        return resolved;
    }

    public static void EnsureId(EntityId value, string parameterName)
    {
        if (value.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }

    public static string RequiredText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The value cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    public static string RequiredContent(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Non-empty content is required.", parameterName);
        }

        return value;
    }

    public static string RequiredJsonObject(string? value, string parameterName)
    {
        var content = RequiredContent(value, parameterName);
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("The JSON artifact must be an object.", parameterName);
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The JSON artifact is invalid.", parameterName, exception);
        }

        return content;
    }

    public static void EnsureDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value is not supported.");
        }
    }
}
