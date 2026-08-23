using Beeexy.Domain.History;

namespace Beeexy.Infrastructure.Persistence;

internal static class ClinicalHistoryPersistence
{
    public const string CompletedPreTriageEventType = "completed_pre_triage";
    public const string PreTriageEpisodeSourceType = "pre_triage_episode";

    public static string StoreEventType(ClinicalHistoryEventType eventType)
    {
        return eventType switch
        {
            ClinicalHistoryEventType.CompletedPreTriage => CompletedPreTriageEventType,
            _ => throw new InvalidOperationException(
                "Unsupported Clinical History event type.")
        };
    }

    public static ClinicalHistoryEventType LoadEventType(string value)
    {
        return value switch
        {
            CompletedPreTriageEventType => ClinicalHistoryEventType.CompletedPreTriage,
            _ => throw new InvalidOperationException(
                "Unsupported persisted Clinical History event type.")
        };
    }

    public static string StoreSourceType(AuthoritativeClinicalSourceType sourceType)
    {
        return sourceType switch
        {
            AuthoritativeClinicalSourceType.PreTriageEpisode =>
                PreTriageEpisodeSourceType,
            _ => throw new InvalidOperationException(
                "Unsupported authoritative clinical source type.")
        };
    }

    public static AuthoritativeClinicalSourceType LoadSourceType(string value)
    {
        return value switch
        {
            PreTriageEpisodeSourceType =>
                AuthoritativeClinicalSourceType.PreTriageEpisode,
            _ => throw new InvalidOperationException(
                "Unsupported persisted authoritative clinical source type.")
        };
    }
}
