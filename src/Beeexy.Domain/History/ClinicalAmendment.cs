using Beeexy.Domain.Common;

namespace Beeexy.Domain.History;

public sealed class ClinicalAmendment
{
    private ClinicalAmendment()
    {
        Reason = null!;
    }

    private ClinicalAmendment(
        EntityId id,
        EntityId clinicalHistoryEventId,
        AuthoritativeSourceReference sourceReference,
        ClinicalSourceProvenance sourceProvenance,
        EntityId authorAccountId,
        AmendmentReason reason,
        DateTimeOffset createdAt,
        EntityId? idempotencyKey)
    {
        Id = id;
        ClinicalHistoryEventId = clinicalHistoryEventId;
        SourceType = sourceReference.SourceType;
        SourceId = sourceReference.SourceId;
        SourceQuestionnaireVersionId = sourceProvenance.QuestionnaireVersionId;
        SourceClinicalRuleSetVersionId = sourceProvenance.ClinicalRuleSetVersionId;
        AuthorAccountId = authorAccountId;
        Reason = reason;
        CreatedAt = createdAt;
        IdempotencyKey = idempotencyKey;
    }

    public EntityId Id { get; private set; }

    public EntityId ClinicalHistoryEventId { get; private set; }

    public AuthoritativeClinicalSourceType SourceType { get; private set; }

    public EntityId SourceId { get; private set; }

    public EntityId SourceQuestionnaireVersionId { get; private set; }

    public EntityId SourceClinicalRuleSetVersionId { get; private set; }

    public EntityId AuthorAccountId { get; private set; }

    public AmendmentReason Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public EntityId? IdempotencyKey { get; private set; }

    public AuthoritativeSourceReference SourceReference =>
        AuthoritativeSourceReference.ForPreTriageEpisode(SourceId);

    public ClinicalSourceProvenance SourceProvenance =>
        ClinicalSourceProvenance.FromCompletedPreTriageSource(
            SourceQuestionnaireVersionId,
            SourceClinicalRuleSetVersionId);

    public static ClinicalAmendment Create(
        ClinicalHistoryEvent historyEvent,
        EntityId authorAccountId,
        AmendmentReason reason,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(historyEvent);
        return CreateCore(
            historyEvent,
            historyEvent.SourceReference,
            historyEvent.SourceProvenance,
            authorAccountId,
            reason,
            createdAt,
            idempotencyKey: null,
            id);
    }

    public static ClinicalAmendment Create(
        ClinicalHistoryEvent historyEvent,
        AuthoritativeSourceReference sourceReference,
        ClinicalSourceProvenance sourceProvenance,
        EntityId authorAccountId,
        AmendmentReason reason,
        DateTimeOffset createdAt,
        EntityId? id = null)
        => CreateCore(
            historyEvent,
            sourceReference,
            sourceProvenance,
            authorAccountId,
            reason,
            createdAt,
            idempotencyKey: null,
            id);

    private static ClinicalAmendment CreateCore(
        ClinicalHistoryEvent historyEvent,
        AuthoritativeSourceReference sourceReference,
        ClinicalSourceProvenance sourceProvenance,
        EntityId authorAccountId,
        AmendmentReason reason,
        DateTimeOffset createdAt,
        EntityId? idempotencyKey,
        EntityId? id)
    {
        ArgumentNullException.ThrowIfNull(historyEvent);
        ArgumentNullException.ThrowIfNull(sourceReference);
        ArgumentNullException.ThrowIfNull(sourceProvenance);
        ArgumentNullException.ThrowIfNull(reason);
        EnsureNonEmpty(authorAccountId, nameof(authorAccountId));
        if (idempotencyKey.HasValue)
        {
            EnsureNonEmpty(idempotencyKey.Value, nameof(idempotencyKey));
        }

        if (sourceReference != historyEvent.SourceReference)
        {
            throw new ArgumentException(
                "The amendment source must match its Clinical History event.",
                nameof(sourceReference));
        }

        if (sourceProvenance != historyEvent.SourceProvenance)
        {
            throw new ArgumentException(
                "The amendment provenance must match its Clinical History event.",
                nameof(sourceProvenance));
        }

        InstantGuard.EnsureNotBefore(
            createdAt,
            historyEvent.RecordedAt,
            nameof(createdAt));

        return new ClinicalAmendment(
            id ?? EntityId.New(),
            historyEvent.Id,
            sourceReference,
            sourceProvenance,
            authorAccountId,
            reason,
            createdAt,
            idempotencyKey);
    }

    public static ClinicalAmendment CreateForRequest(
        ClinicalHistoryEvent historyEvent,
        EntityId authorAccountId,
        AmendmentReason reason,
        DateTimeOffset createdAt,
        EntityId idempotencyKey,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(historyEvent);
        return CreateCore(
            historyEvent,
            historyEvent.SourceReference,
            historyEvent.SourceProvenance,
            authorAccountId,
            reason,
            createdAt,
            idempotencyKey,
            id);
    }

    private static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }
}
