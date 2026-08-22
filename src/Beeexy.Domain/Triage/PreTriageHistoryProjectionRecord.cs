using Beeexy.Domain.Common;

namespace Beeexy.Domain.Triage;

public sealed class PreTriageHistoryProjectionRecord
{
    private PreTriageHistoryProjectionRecord()
    {
    }

    private PreTriageHistoryProjectionRecord(
        EntityId sourceEpisodeId,
        EntityId patientProfileId,
        DateTimeOffset completedAt,
        DateTimeOffset createdAt)
    {
        SourceEpisodeId = sourceEpisodeId;
        PatientProfileId = patientProfileId;
        CompletedAt = completedAt;
        CreatedAt = createdAt;
    }

    public EntityId SourceEpisodeId { get; private set; }

    public EntityId PatientProfileId { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static PreTriageHistoryProjectionRecord Create(
        PreTriageEpisode episode,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(episode);
        if (episode.PatientProfileId is null)
        {
            throw new InvalidOperationException(
                "Only a patient-owned pre-triage episode can enter Clinical History.");
        }

        InstantGuard.EnsureNotBefore(createdAt, episode.CompletedAt, nameof(createdAt));
        return new PreTriageHistoryProjectionRecord(
            episode.Id,
            episode.PatientProfileId.Value,
            episode.CompletedAt,
            createdAt);
    }
}
