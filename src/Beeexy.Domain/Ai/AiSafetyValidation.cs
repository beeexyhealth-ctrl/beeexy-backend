using Beeexy.Domain.Common;

namespace Beeexy.Domain.Ai;

public sealed class AiSafetyValidation
{
    private AiSafetyValidation()
    {
        PolicyVersion = null!;
    }

    private AiSafetyValidation(
        EntityId id,
        EntityId executionId,
        EntityId? resultSnapshotId,
        AiSafetyCategory category,
        string policyVersion,
        string? productContentVersion,
        bool displayEligible,
        string? restrictedAuditOutput,
        DateTimeOffset createdAt)
    {
        Id = id;
        ExecutionId = executionId;
        ResultSnapshotId = resultSnapshotId;
        Category = category;
        PolicyVersion = policyVersion;
        ProductContentVersion = productContentVersion;
        DisplayEligible = displayEligible;
        RestrictedAuditOutput = restrictedAuditOutput;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId ExecutionId { get; private set; }

    public EntityId? ResultSnapshotId { get; private set; }

    public AiSafetyCategory Category { get; private set; }

    public string PolicyVersion { get; private set; }

    public string? ProductContentVersion { get; private set; }

    public bool DisplayEligible { get; private set; }

    public string? RestrictedAuditOutput { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AiSafetyValidation CreateApproved(
        EntityId executionId,
        EntityId resultSnapshotId,
        string policyVersion,
        DateTimeOffset createdAt,
        string? productContentVersion = null,
        EntityId? id = null)
    {
        AiGuard.EnsureId(executionId, nameof(executionId));
        AiGuard.EnsureId(resultSnapshotId, nameof(resultSnapshotId));
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        return new AiSafetyValidation(
            AiGuard.IdOrNew(id, nameof(id)),
            executionId,
            resultSnapshotId,
            AiSafetyCategory.Approved,
            RequiredPolicyVersion(policyVersion),
            OptionalProductContentVersion(productContentVersion),
            true,
            null,
            createdAt);
    }

    public static AiSafetyValidation CreateRejected(
        EntityId executionId,
        AiSafetyCategory category,
        string policyVersion,
        string restrictedAuditOutput,
        DateTimeOffset createdAt,
        string? productContentVersion = null,
        EntityId? id = null)
    {
        AiGuard.EnsureId(executionId, nameof(executionId));
        AiGuard.EnsureDefined(category, nameof(category));
        if (category == AiSafetyCategory.Approved)
        {
            throw new ArgumentOutOfRangeException(
                nameof(category),
                "Approved output must use the approved validation factory.");
        }

        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        return new AiSafetyValidation(
            AiGuard.IdOrNew(id, nameof(id)),
            executionId,
            null,
            category,
            RequiredPolicyVersion(policyVersion),
            OptionalProductContentVersion(productContentVersion),
            false,
            AiGuard.RequiredContent(restrictedAuditOutput, nameof(restrictedAuditOutput)),
            createdAt);
    }

    private static string RequiredPolicyVersion(string value)
    {
        return AiGuard.RequiredText(
            value,
            AiPersistenceLimits.PolicyVersion,
            nameof(value));
    }

    private static string? OptionalProductContentVersion(string? value)
    {
        return value is null
            ? null
            : AiGuard.RequiredText(
                value,
                AiPersistenceLimits.ProductContentVersion,
                nameof(value));
    }
}
