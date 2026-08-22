namespace Beeexy.Domain.Triage;

public static class ClinicalUrgencies
{
    public static UrgencyCode VeryLow { get; } = UrgencyCode.Create("VERY_LOW");

    public static UrgencyCode Low { get; } = UrgencyCode.Create("LOW");

    public static UrgencyCode Medium { get; } = UrgencyCode.Create("MEDIUM");

    public static UrgencyCode High { get; } = UrgencyCode.Create("HIGH");

    public static UrgencyCode Critical { get; } = UrgencyCode.Create("CRITICAL");

    public static IReadOnlyDictionary<UrgencyCode, int> SeverityOrder { get; } =
        new Dictionary<UrgencyCode, int>
        {
            [VeryLow] = 0,
            [Low] = 1,
            [Medium] = 2,
            [High] = 3,
            [Critical] = 4
        };
}
