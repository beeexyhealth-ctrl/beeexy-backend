using Beeexy.Domain.Common;

namespace Beeexy.Domain.Triage;

public sealed class PreTriageSession
{
    private readonly List<TriageAnswer> _answers = [];
    private readonly List<ReportedSymptom> _reportedSymptoms = [];

    private PreTriageSession()
    {
    }

    private PreTriageSession(
        EntityId id,
        EntityId? patientProfileId,
        EntityId questionnaireVersionId,
        AnonymousCapabilityHash? anonymousCapabilityHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt)
    {
        Id = id;
        PatientProfileId = patientProfileId;
        QuestionnaireVersionId = questionnaireVersionId;
        AnonymousCapabilityHash = anonymousCapabilityHash;
        ExpiresAt = expiresAt;
        Status = PreTriageSessionStatus.Active;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId? PatientProfileId { get; private set; }

    public EntityId QuestionnaireVersionId { get; private set; }

    public AnonymousCapabilityHash? AnonymousCapabilityHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public PreTriageSessionStatus Status { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public IReadOnlyCollection<TriageAnswer> Answers => _answers.AsReadOnly();

    public IReadOnlyCollection<ReportedSymptom> ReportedSymptoms => _reportedSymptoms.AsReadOnly();

    public bool IsAnonymous => PatientProfileId is null;

    public static PreTriageSession CreateAnonymous(
        EntityId questionnaireVersionId,
        AnonymousCapabilityHash anonymousCapabilityHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(anonymousCapabilityHash);
        return Create(
            id ?? EntityId.New(),
            null,
            questionnaireVersionId,
            anonymousCapabilityHash,
            expiresAt,
            createdAt);
    }

    public static PreTriageSession CreateForPatient(
        EntityId patientProfileId,
        EntityId questionnaireVersionId,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        EnsureNonEmpty(patientProfileId, nameof(patientProfileId));
        return Create(
            id ?? EntityId.New(),
            patientProfileId,
            questionnaireVersionId,
            null,
            expiresAt,
            createdAt);
    }

    public TriageAnswer RecordAnswer(
        TriageQuestion question,
        string answerJson,
        int sequence,
        DateTimeOffset recordedAt,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(question);
        EnsureActiveAt(recordedAt);
        if (question.QuestionnaireVersionId != QuestionnaireVersionId)
        {
            throw new ArgumentException(
                "The question must belong to the session questionnaire version.",
                nameof(question));
        }

        if (_answers.Any(answer => answer.Sequence == sequence))
        {
            throw new InvalidOperationException("Answer sequence must be unique within a session.");
        }

        var answer = TriageAnswer.CreateForSession(
            Id,
            QuestionnaireVersionId,
            question.Id,
            answerJson,
            sequence,
            recordedAt,
            id);
        _answers.Add(answer);
        UpdatedAt = recordedAt;
        return answer;
    }

    public ReportedSymptom ReportSymptom(
        SymptomText originalText,
        int sequence,
        DateTimeOffset reportedAt,
        string? terminologySystem = null,
        string? terminologyCode = null,
        string? terminologyDisplay = null,
        string? normalizationSource = null,
        DateTimeOffset? normalizedAt = null,
        EntityId? id = null)
    {
        EnsureActiveAt(reportedAt);
        if (_reportedSymptoms.Any(symptom => symptom.Sequence == sequence))
        {
            throw new InvalidOperationException("Symptom sequence must be unique within a session.");
        }

        var symptom = ReportedSymptom.CreateForSession(
            Id,
            originalText,
            sequence,
            reportedAt,
            terminologySystem,
            terminologyCode,
            terminologyDisplay,
            normalizationSource,
            normalizedAt,
            id);
        _reportedSymptoms.Add(symptom);
        UpdatedAt = reportedAt;
        return symptom;
    }

    internal CompletedWorkflowData CompleteInto(
        EntityId episodeId,
        DateTimeOffset completedAt)
    {
        EnsureNonEmpty(episodeId, nameof(episodeId));
        EnsureActiveAt(completedAt);

        Status = PreTriageSessionStatus.Completed;
        CompletedAt = completedAt;
        UpdatedAt = completedAt;
        foreach (var answer in _answers)
        {
            answer.PromoteToEpisode(episodeId);
        }

        foreach (var symptom in _reportedSymptoms)
        {
            symptom.PromoteToEpisode(episodeId);
        }

        var completedData = new CompletedWorkflowData(
            _answers.ToArray(),
            _reportedSymptoms.ToArray());
        _answers.Clear();
        _reportedSymptoms.Clear();
        return completedData;
    }

    private static PreTriageSession Create(
        EntityId id,
        EntityId? patientProfileId,
        EntityId questionnaireVersionId,
        AnonymousCapabilityHash? anonymousCapabilityHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt)
    {
        EnsureNonEmpty(id, nameof(id));
        EnsureNonEmpty(questionnaireVersionId, nameof(questionnaireVersionId));
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        InstantGuard.EnsureUtc(expiresAt, nameof(expiresAt));
        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "Session expiration must follow creation time.");
        }

        return new PreTriageSession(
            id,
            patientProfileId,
            questionnaireVersionId,
            anonymousCapabilityHash,
            expiresAt,
            createdAt);
    }

    private void EnsureActiveAt(DateTimeOffset instant)
    {
        InstantGuard.EnsureNotBefore(instant, CreatedAt, nameof(instant));
        if (Status != PreTriageSessionStatus.Active)
        {
            throw new InvalidOperationException("Only an active pre-triage session can be changed.");
        }

        if (instant >= ExpiresAt)
        {
            throw new InvalidOperationException("An expired pre-triage session cannot be changed.");
        }
    }

    private static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }

    internal sealed record CompletedWorkflowData(
        IReadOnlyCollection<TriageAnswer> Answers,
        IReadOnlyCollection<ReportedSymptom> ReportedSymptoms);
}
