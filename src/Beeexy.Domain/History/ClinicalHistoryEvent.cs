using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Domain.History;

public sealed class ClinicalHistoryEvent
{
    private ClinicalHistoryEvent()
    {
    }

    private ClinicalHistoryEvent(
        EntityId id,
        EntityId patientProfileId,
        ClinicalHistoryEventType eventType,
        AuthoritativeSourceReference sourceReference,
        ClinicalSourceProvenance sourceProvenance,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt)
    {
        Id = id;
        PatientProfileId = patientProfileId;
        EventType = eventType;
        SourceType = sourceReference.SourceType;
        SourceId = sourceReference.SourceId;
        SourceQuestionnaireVersionId = sourceProvenance.QuestionnaireVersionId;
        SourceClinicalRuleSetVersionId = sourceProvenance.ClinicalRuleSetVersionId;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
    }

    public EntityId Id { get; private set; }

    public EntityId PatientProfileId { get; private set; }

    public ClinicalHistoryEventType EventType { get; private set; }

    public AuthoritativeClinicalSourceType SourceType { get; private set; }

    public EntityId SourceId { get; private set; }

    public EntityId SourceQuestionnaireVersionId { get; private set; }

    public EntityId SourceClinicalRuleSetVersionId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public AuthoritativeSourceReference SourceReference =>
        AuthoritativeSourceReference.ForPreTriageEpisode(SourceId);

    public ClinicalSourceProvenance SourceProvenance =>
        ClinicalSourceProvenance.FromCompletedPreTriageSource(
            SourceQuestionnaireVersionId,
            SourceClinicalRuleSetVersionId);

    public static ClinicalHistoryEvent CreateCompletedPreTriage(
        PreTriageEpisode sourceEpisode,
        DateTimeOffset recordedAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(sourceEpisode);
        return Create(
            sourceEpisode,
            ClinicalHistoryEventType.CompletedPreTriage,
            AuthoritativeSourceReference.ForPreTriageEpisode(sourceEpisode.Id),
            ClinicalSourceProvenance.FromCompletedPreTriage(sourceEpisode),
            recordedAt,
            id);
    }

    public static ClinicalHistoryEvent Create(
        PreTriageEpisode sourceEpisode,
        ClinicalHistoryEventType eventType,
        AuthoritativeSourceReference sourceReference,
        ClinicalSourceProvenance sourceProvenance,
        DateTimeOffset recordedAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(sourceEpisode);
        ArgumentNullException.ThrowIfNull(sourceReference);
        ArgumentNullException.ThrowIfNull(sourceProvenance);

        if (eventType != ClinicalHistoryEventType.CompletedPreTriage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventType),
                "The clinical history event type is not supported.");
        }

        var expectedReference =
            AuthoritativeSourceReference.ForPreTriageEpisode(sourceEpisode.Id);
        if (sourceReference != expectedReference)
        {
            throw new ArgumentException(
                "The authoritative source reference does not identify the episode.",
                nameof(sourceReference));
        }

        var expectedProvenance =
            ClinicalSourceProvenance.FromCompletedPreTriage(sourceEpisode);
        if (sourceProvenance != expectedProvenance)
        {
            throw new ArgumentException(
                "The source provenance does not match the completed episode versions.",
                nameof(sourceProvenance));
        }

        if (!sourceEpisode.PatientProfileId.HasValue)
        {
            throw new InvalidOperationException(
                "Only a patient-owned completed pre-triage episode can enter Clinical History.");
        }

        InstantGuard.EnsureNotBefore(
            recordedAt,
            sourceEpisode.CompletedAt,
            nameof(recordedAt));

        return new ClinicalHistoryEvent(
            id ?? EntityId.New(),
            sourceEpisode.PatientProfileId.Value,
            eventType,
            sourceReference,
            sourceProvenance,
            sourceEpisode.CompletedAt,
            recordedAt);
    }
}
