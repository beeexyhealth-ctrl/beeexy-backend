using Beeexy.Domain.Common;

namespace Beeexy.Domain.Triage;

public sealed class ClinicalFinding
{
    public const int MaximumCodeLength = 100;

    private ClinicalFinding()
    {
        FindingCode = null!;
        SourceRuleCode = null!;
    }

    private ClinicalFinding(
        EntityId id,
        EntityId assessmentId,
        string findingCode,
        string sourceRuleCode,
        string? messageReference,
        DateTimeOffset createdAt)
    {
        Id = id;
        AssessmentId = assessmentId;
        FindingCode = findingCode;
        SourceRuleCode = sourceRuleCode;
        MessageReference = messageReference;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId AssessmentId { get; private set; }

    public string FindingCode { get; private set; }

    public string SourceRuleCode { get; private set; }

    public string? MessageReference { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static ClinicalFinding Create(
        EntityId assessmentId,
        string findingCode,
        string sourceRuleCode,
        string? messageReference,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        return new ClinicalFinding(
            id ?? EntityId.New(),
            assessmentId,
            TriageValueGuard.RequiredIdentifier(
                findingCode,
                MaximumCodeLength,
                nameof(findingCode)),
            TriageValueGuard.RequiredIdentifier(
                sourceRuleCode,
                MaximumCodeLength,
                nameof(sourceRuleCode)),
            TriageValueGuard.OptionalText(
                messageReference,
                TriagePersistenceLimits.MaximumReferenceLength,
                nameof(messageReference)),
            createdAt);
    }
}
