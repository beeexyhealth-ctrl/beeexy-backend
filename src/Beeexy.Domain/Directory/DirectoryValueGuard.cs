using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

internal static class DirectoryValueGuard
{
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

    public static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }
}
