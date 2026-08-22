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
        ClinicalPathwayCode pathway,
        RuleSetCode ruleSetCode,
        DefinitionVersion version,
        DefinitionHash contentHash,
        ClinicalContentStatus contentStatus,
        string definitionMetadataJson,
        string? sourceReference,
        DateTimeOffset importedAt,
        DateTimeOffset? approvedAt,
        DateTimeOffset? activatedAt)
    {
        Id = id;
        Pathway = pathway;
        RuleSetCode = ruleSetCode;
        Version = version;
        ContentHash = contentHash;
        ContentSource = contentStatus.Source;
        ReviewStatus = contentStatus.ReviewStatus;
        ApprovalStatus = contentStatus.ApprovalStatus;
        DefinitionMetadataJson = definitionMetadataJson;
        SourceReference = sourceReference;
        ImportedAt = importedAt;
        ApprovedAt = approvedAt;
        ActivatedAt = activatedAt;
    }

    public EntityId Id { get; private set; }

    public ClinicalPathwayCode Pathway { get; private set; } = null!;

    public RuleSetCode RuleSetCode { get; private set; }

    public DefinitionVersion Version { get; private set; }

    public DefinitionHash ContentHash { get; private set; }

    public ClinicalContentSource ContentSource { get; private set; }

    public ClinicalReviewStatus ReviewStatus { get; private set; }

    public ClinicalApprovalStatus ApprovalStatus { get; private set; }

    public ClinicalContentStatus ContentStatus => new(
        ContentSource,
        ReviewStatus,
        ApprovalStatus);

    public string DefinitionMetadataJson { get; private set; } = null!;

    public string? SourceReference { get; private set; }

    public DateTimeOffset ImportedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

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
        return Import(
            ClinicalPathwayCode.Create("UNSPECIFIED"),
            ruleSetCode,
            version,
            contentHash,
            ClinicalContentStatus.LegacyApproved,
            "{}",
            importedAt,
            approvedAt,
            activatedAt,
            sourceReference,
            id);
    }

    public static ClinicalRuleSetVersion Import(
        ClinicalPathwayCode pathway,
        RuleSetCode ruleSetCode,
        DefinitionVersion version,
        DefinitionHash contentHash,
        ClinicalContentStatus contentStatus,
        string definitionMetadataJson,
        DateTimeOffset importedAt,
        DateTimeOffset? approvedAt = null,
        DateTimeOffset? activatedAt = null,
        string? sourceReference = null,
        EntityId? id = null)
    {
        ArgumentNullException.ThrowIfNull(pathway);
        ArgumentNullException.ThrowIfNull(ruleSetCode);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(contentHash);
        ArgumentNullException.ThrowIfNull(contentStatus);
        InstantGuard.EnsureUtc(importedAt, nameof(importedAt));
        if (approvedAt.HasValue)
        {
            InstantGuard.EnsureUtc(approvedAt.Value, nameof(approvedAt));
        }

        if (contentStatus.ApprovalStatus == ClinicalApprovalStatus.Approved &&
            !approvedAt.HasValue)
        {
            throw new ArgumentException(
                "Approved clinical content requires an approval timestamp.",
                nameof(approvedAt));
        }

        if (contentStatus.ApprovalStatus != ClinicalApprovalStatus.Approved &&
            approvedAt.HasValue)
        {
            throw new ArgumentException(
                "Unapproved clinical content cannot have an approval timestamp.",
                nameof(approvedAt));
        }

        if (activatedAt.HasValue)
        {
            InstantGuard.EnsureUtc(activatedAt.Value, nameof(activatedAt));
            if (activatedAt < importedAt ||
                (approvedAt.HasValue && activatedAt < approvedAt.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activatedAt),
                    "Activation cannot precede import or approval.");
            }
        }

        return new ClinicalRuleSetVersion(
            id ?? EntityId.New(),
            pathway,
            ruleSetCode,
            version,
            contentHash,
            contentStatus,
            TriageValueGuard.RequiredJson(definitionMetadataJson, nameof(definitionMetadataJson)),
            TriageValueGuard.OptionalText(
                sourceReference,
                TriagePersistenceLimits.MaximumReferenceLength,
                nameof(sourceReference)),
            importedAt,
            approvedAt,
            activatedAt);
    }
}
