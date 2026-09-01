using Beeexy.Domain.Common;

namespace Beeexy.Domain.Ai;

public sealed class AiExecution
{
    private AiExecution()
    {
    }

    private AiExecution(EntityId id, EntityId analysisRequestId, DateTimeOffset createdAt)
    {
        Id = id;
        AnalysisRequestId = analysisRequestId;
        Status = AiExecutionStatus.Pending;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; private set; }

    public EntityId AnalysisRequestId { get; private set; }

    public AiExecutionStatus Status { get; private set; }

    public string? ProviderIdentifier { get; private set; }

    public string? ModelIdentifier { get; private set; }

    public string? PromptVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public long? LatencyMilliseconds { get; private set; }

    public string? SanitizedFailureCategory { get; private set; }

    public static AiExecution CreatePending(
        EntityId analysisRequestId,
        DateTimeOffset createdAt,
        EntityId? id = null)
    {
        AiGuard.EnsureId(analysisRequestId, nameof(analysisRequestId));
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        return new AiExecution(
            AiGuard.IdOrNew(id, nameof(id)),
            analysisRequestId,
            createdAt);
    }

    public void Start(
        string providerIdentifier,
        string modelIdentifier,
        string promptVersion,
        DateTimeOffset startedAt)
    {
        EnsureStatus(AiExecutionStatus.Pending);
        InstantGuard.EnsureNotBefore(startedAt, CreatedAt, nameof(startedAt));

        ProviderIdentifier = AiGuard.RequiredText(
            providerIdentifier,
            AiPersistenceLimits.Identifier,
            nameof(providerIdentifier));
        ModelIdentifier = AiGuard.RequiredText(
            modelIdentifier,
            AiPersistenceLimits.ModelIdentifier,
            nameof(modelIdentifier));
        PromptVersion = AiGuard.RequiredText(
            promptVersion,
            AiPersistenceLimits.Identifier,
            nameof(promptVersion));
        StartedAt = startedAt;
        Status = AiExecutionStatus.Running;
    }

    public void MarkSucceeded(DateTimeOffset completedAt)
    {
        Complete(AiExecutionStatus.Succeeded, completedAt, null);
    }

    public void MarkFailed(string sanitizedFailureCategory, DateTimeOffset completedAt)
    {
        var category = AiGuard.RequiredText(
            sanitizedFailureCategory,
            AiPersistenceLimits.FailureCategory,
            nameof(sanitizedFailureCategory));
        Complete(AiExecutionStatus.Failed, completedAt, category);
    }

    public void MarkRejected(DateTimeOffset completedAt)
    {
        Complete(AiExecutionStatus.Rejected, completedAt, null);
    }

    private void Complete(
        AiExecutionStatus terminalStatus,
        DateTimeOffset completedAt,
        string? sanitizedFailureCategory)
    {
        EnsureStatus(AiExecutionStatus.Running);
        if (terminalStatus is not AiExecutionStatus.Succeeded and
            not AiExecutionStatus.Failed and
            not AiExecutionStatus.Rejected)
        {
            throw new ArgumentOutOfRangeException(
                nameof(terminalStatus),
                "The execution terminal status is not supported.");
        }

        InstantGuard.EnsureNotBefore(completedAt, StartedAt!.Value, nameof(completedAt));
        Status = terminalStatus;
        CompletedAt = completedAt;
        LatencyMilliseconds = checked((long)(completedAt - StartedAt.Value).TotalMilliseconds);
        SanitizedFailureCategory = sanitizedFailureCategory;
    }

    private void EnsureStatus(AiExecutionStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"AI execution must be {expected} for this transition.");
        }
    }
}
