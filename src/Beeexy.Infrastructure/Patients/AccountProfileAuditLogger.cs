using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Patients;

public sealed class AccountProfileAuditLogger(
    ILogger<AccountProfileAuditLogger> logger) : IAccountProfileAuditLogger
{
    public void InvariantViolation(EntityId accountId, string invariant)
    {
        logger.LogError(
            "Current account/profile invariant {Invariant} failed for account {AccountId}.",
            invariant,
            accountId.Value);
    }

    public void ProfileUpdateSucceeded(
        EntityId accountId,
        EntityId profileId,
        IReadOnlyCollection<string> changedFields,
        DateTimeOffset occurredAt)
    {
        logger.LogInformation(
            "Primary profile update succeeded for account {AccountId}, profile {ProfileId}, " +
            "changed fields {ChangedFields}, at {OccurredAt}.",
            accountId.Value,
            profileId.Value,
            string.Join(',', changedFields),
            occurredAt);
    }

    public void ProfileUpdateConflict(EntityId accountId, EntityId profileId)
    {
        logger.LogWarning(
            "Primary profile update conflict for account {AccountId}, profile {ProfileId}.",
            accountId.Value,
            profileId.Value);
    }
}
