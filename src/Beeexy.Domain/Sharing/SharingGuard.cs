using Beeexy.Domain.Common;

namespace Beeexy.Domain.Sharing;

internal static class SharingGuard
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

    public static void EnsureDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value is not supported.");
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

        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Control characters are not permitted.", parameterName);
        }

        return normalized;
    }
}
