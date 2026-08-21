using Beeexy.Domain.Common;

namespace Beeexy.Domain.Triage;

public sealed class ClinicalRuleSetVersion
{
    private ClinicalRuleSetVersion()
    {
        RuleSetCode = null!;
        Version = null!;
        ContentHash = null!;
    }

    private ClinicalRuleSetVersion(
        EntityId id,
        RuleSetCode ruleSetCode,
        DefinitionVersion version,
        DefinitionHash contentHash,
        string? sourceReference,
        DateTimeOffset importedAt,
        DateTimeOffset approvedAt,
        DateTimeOffset? activatedAt)
    {
        Id = id;
        RuleSetCode = ruleSetCode;
        Version = version;
        ContentHash = contentHash;
        SourceReference = sourceReference;
        ImportedAt = importedAt;
        ApprovedAt = approvedAt;
        ActivatedAt = activatedAt;
    }

    public EntityId Id { get; private set; }

    public RuleSetCode RuleSetCode { get; private set; }

    public DefinitionVersion Version { get; private set; }

    public DefinitionHash ContentHash { get; private set; }

    public string? SourceReference { get; private set; }

    public DateTimeOffset ImportedAt { get; private set; }

    public DateTimeOffset ApprovedAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public static ClinicalRuleSetVersion ImportApproved(
        RuleSetCode ruleSetCode,
        DefinitionVersion version,
        DefinitionHash contentHash,
        DateTimeOffset importedAt,
        DateTimeOffset approvedAt,
        DateTimeOffset? activatedAt = null,
        string? sourceReference = null,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(ruleSetCode);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(contentHash);
        InstantGuard.EnsureUtc(importedAt, nameof(importedAt));
        InstantGuard.EnsureUtc(approvedAt, nameof(approvedAt));
        if (activatedAt.HasValue)
        {
            InstantGuard.EnsureUtc(activatedAt.Value, nameof(activatedAt));
            if (activatedAt < importedAt || activatedAt < approvedAt)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activatedAt),
                    "Activation cannot precede import or approval.");
            }
        }

        return new ClinicalRuleSetVersion(
            id ?? EntityId.New(),
            ruleSetCode,
            version,
            contentHash,
            TriageValueGuard.OptionalText(
                sourceReference,
                TriagePersistenceLimits.MaximumReferenceLength,
                nameof(sourceReference)),
            importedAt,
            approvedAt,
            activatedAt);
    }
}
