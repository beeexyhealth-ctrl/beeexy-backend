using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Domain.Care;

public sealed class SymptomCheckIn
{
    private readonly List<SymptomCheckInAnswer> _answers = [];

    private SymptomCheckIn()
    {
        CanonicalRequestHash = null!;
    }

    private SymptomCheckIn(
        EntityId id,
        EntityId episodeId,
        EntityId packageVersionId,
        EntityId submittingAccountId,
        DateTimeOffset createdAt,
        EntityId idempotencyKey,
        SymptomDiarySha256 canonicalRequestHash)
    {
        Id = id;
        EpisodeId = episodeId;
        PackageVersionId = packageVersionId;
        SubmittingAccountId = submittingAccountId;
        CreatedAt = createdAt;
        IdempotencyKey = idempotencyKey;
        CanonicalRequestHash = canonicalRequestHash;
    }

    public EntityId Id { get; private set; }
    public EntityId EpisodeId { get; private set; }
    public EntityId PackageVersionId { get; private set; }
    public EntityId SubmittingAccountId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public EntityId IdempotencyKey { get; private set; }
    public SymptomDiarySha256 CanonicalRequestHash { get; private set; }
    public IReadOnlyCollection<SymptomCheckInAnswer> Answers => _answers.AsReadOnly();

    public static SymptomCheckIn Create(
        PreTriageEpisode episode,
        QuestionnaireDefinitionVersion frozenQuestionnaire,
        SymptomDiaryPackageVersion package,
        EntityId submittingAccountId,
        EntityId idempotencyKey,
        SymptomDiarySha256 canonicalRequestHash,
        DateTimeOffset createdAt,
        IEnumerable<SymptomCheckInAnswerInput>? answers = null,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(frozenQuestionnaire);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(canonicalRequestHash);
        if (!episode.PatientProfileId.HasValue)
        {
            throw new ArgumentException("A check-in requires a patient-owned episode.", nameof(episode));
        }

        if (episode.QuestionnaireVersionId != frozenQuestionnaire.Id)
        {
            throw new ArgumentException(
                "The questionnaire must be the episode's frozen version.",
                nameof(frozenQuestionnaire));
        }

        if (package.Pathway != frozenQuestionnaire.Pathway)
        {
            throw new ArgumentException(
                "The diary package pathway must match the episode pathway.",
                nameof(package));
        }

        SymptomDiaryGuard.EnsureId(submittingAccountId, nameof(submittingAccountId));
        SymptomDiaryGuard.EnsureId(idempotencyKey, nameof(idempotencyKey));
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        var entityId = id ?? EntityId.New();
        SymptomDiaryGuard.EnsureId(entityId, nameof(id));
        var checkIn = new SymptomCheckIn(
            entityId,
            episode.Id,
            package.Id,
            submittingAccountId,
            createdAt,
            idempotencyKey,
            canonicalRequestHash);

        foreach (var answer in (answers ?? []).OrderBy(value => value.Question.SourceOrder))
        {
            ArgumentNullException.ThrowIfNull(answer);
            if (checkIn._answers.Any(existing => existing.QuestionId == answer.Question.Id))
            {
                throw new InvalidOperationException("A check-in may contain only one answer per question.");
            }

            checkIn._answers.Add(SymptomCheckInAnswer.Create(
                entityId,
                package.Id,
                answer.Question,
                answer.SubmittedValueJson,
                createdAt,
                answer.Id));
        }

        return checkIn;
    }
}
