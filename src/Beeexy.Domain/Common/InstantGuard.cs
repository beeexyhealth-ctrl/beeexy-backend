namespace Beeexy.Domain.Common;

internal static class InstantGuard
{
    public static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be expressed in UTC.", parameterName);
        }
    }

    public static void EnsureNotBefore(
        DateTimeOffset value,
        DateTimeOffset earliest,
        string parameterName)
    {
        EnsureUtc(value, parameterName);

        if (value < earliest)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The timestamp cannot precede the entity creation time.");
        }
    }
}
