using Beeexy.Domain.Common;

namespace Beeexy.Domain.Triage;

public sealed class PreTriageEpisode
{
    private readonly List<TriageAnswer> _answers = [];
    private readonly List<ReportedSymptom> _reportedSymptoms = [];

    private PreTriageEpisode()
    {
    }

    private PreTriageEpisode(
        EntityId id,
        EntityId sourceSessionId,
        EntityId? patientProfileId,
        EntityId questionnaireVersionId,
        EntityId clinicalRuleSetVersionId,
        DateTimeOffset completedAt,
        DateTimeOffset? anonymousExpiresAt,
        IEnumerable<TriageAnswer> answers,
        IEnumerable<ReportedSymptom> reportedSymptoms)
    {
        Id = id;
        SourceSessionId = sourceSessionId;
        PatientProfileId = patientProfileId;
        QuestionnaireVersionId = questionnaireVersionId;
        ClinicalRuleSetVersionId = clinicalRuleSetVersionId;
        CompletedAt = completedAt;
        AnonymousExpiresAt = anonymousExpiresAt;
        _answers.AddRange(answers);
        _reportedSymptoms.AddRange(reportedSymptoms);
    }

    public EntityId Id { get; private set; }

    public EntityId SourceSessionId { get; private set; }

    public EntityId? PatientProfileId { get; private set; }

    public EntityId QuestionnaireVersionId { get; private set; }

    public EntityId ClinicalRuleSetVersionId { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    public DateTimeOffset? AnonymousExpiresAt { get; private set; }

    public DateTimeOffset? ClaimedAt { get; private set; }

    public IReadOnlyCollection<TriageAnswer> Answers => _answers.AsReadOnly();

    public IReadOnlyCollection<ReportedSymptom> ReportedSymptoms =>
        _reportedSymptoms.AsReadOnly();

    public bool IsClaimed => ClaimedAt.HasValue;

    public static PreTriageEpisode CreateFrom(
        PreTriageSession session,
        EntityId clinicalRuleSetVersionId,
        DateTimeOffset completedAt,
        DateTimeOffset? anonymousExpiresAt = null,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureNonEmpty(clinicalRuleSetVersionId, nameof(clinicalRuleSetVersionId));
        InstantGuard.EnsureUtc(completedAt, nameof(completedAt));

        if (session.IsAnonymous)
        {
            if (!anonymousExpiresAt.HasValue)
            {
                throw new ArgumentException(
                    "An anonymous episode requires an unclaimed expiration timestamp.",
                    nameof(anonymousExpiresAt));
            }

            InstantGuard.EnsureUtc(anonymousExpiresAt.Value, nameof(anonymousExpiresAt));
            if (anonymousExpiresAt <= completedAt)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(anonymousExpiresAt),
                    "Anonymous episode expiration must follow completion.");
            }
        }
        else if (anonymousExpiresAt.HasValue)
        {
            throw new ArgumentException(
                "A patient-associated episode cannot have anonymous expiration metadata.",
                nameof(anonymousExpiresAt));
        }

        var episodeId = id ?? EntityId.New();
        var completedData = session.CompleteInto(episodeId, completedAt);
        return new PreTriageEpisode(
            episodeId,
            session.Id,
            session.PatientProfileId,
            session.QuestionnaireVersionId,
            clinicalRuleSetVersionId,
            completedAt,
            anonymousExpiresAt,
            completedData.Answers,
            completedData.ReportedSymptoms);
    }

    public bool Claim(EntityId patientProfileId, DateTimeOffset claimedAt)
    {
        EnsureNonEmpty(patientProfileId, nameof(patientProfileId));
        InstantGuard.EnsureNotBefore(claimedAt, CompletedAt, nameof(claimedAt));

        if (PatientProfileId.HasValue)
        {
            if (IsClaimed && PatientProfileId == patientProfileId)
            {
                return false;
            }

            throw new InvalidOperationException("The episode already belongs to a patient.");
        }

        if (!AnonymousExpiresAt.HasValue || claimedAt >= AnonymousExpiresAt)
        {
            throw new InvalidOperationException("The anonymous episode can no longer be claimed.");
        }

        PatientProfileId = patientProfileId;
        ClaimedAt = claimedAt;
        return true;
    }

    private static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }
}
