using System.Text.Json;

namespace Beeexy.Domain.Triage;

internal static class TriageValueGuard
{
    public const int MaximumJsonLength = 65536;
    public static string RequiredIdentifier(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var candidate = value.Trim();
        if (candidate.Length > maximumLength || candidate.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("The identifier is invalid.", parameterName);
        }

        return candidate;
    }

    public static string RequiredText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var candidate = value.Trim();
        if (candidate.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return candidate;
    }

    public static string? OptionalText(string? value, int maximumLength, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        return RequiredText(value, maximumLength, parameterName);
    }

    public static string? OptionalJson(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        return RequiredJson(value, parameterName);
    }

    public static string RequiredJson(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumJsonLength)
        {
            throw new ArgumentException(
                $"JSON content cannot exceed {MaximumJsonLength} characters.",
                parameterName);
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The value must contain valid JSON.", parameterName, exception);
        }

        return value;
    }
}
