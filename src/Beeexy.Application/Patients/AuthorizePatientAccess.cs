using Beeexy.Domain.Common;

namespace Beeexy.Application.Patients;

public sealed class AuthorizePatientAccess(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    IPatientAccessAuthorizationRepository repository,
    IMyCircleAuditLogger auditLogger)
{
    public async Task<PatientAccessAuthorizationResult> ExecuteAsync(
        EntityId targetProfileId,
        CancellationToken cancellationToken = default) =>
        await ExecuteCoreAsync(
            targetProfileId,
            lockActiveRelationship: false,
            current: null,
            cancellationToken);

    internal async Task<PatientAccessAuthorizationResult> ExecuteAsync(
        EntityId targetProfileId,
        ResolvedCurrentAccountProfile current,
        CancellationToken cancellationToken = default) =>
        await ExecuteCoreAsync(
            targetProfileId,
            lockActiveRelationship: false,
            current,
            cancellationToken);

    internal async Task<PatientAccessAuthorizationResult> ExecuteForPatientUpdateAsync(
        EntityId targetProfileId,
        CancellationToken cancellationToken = default) =>
        await ExecuteCoreAsync(
            targetProfileId,
            lockActiveRelationship: true,
            current: null,
            cancellationToken);

    internal async Task<PatientAccessAuthorizationResult> ExecuteForPatientUpdateAsync(
        EntityId targetProfileId,
        ResolvedCurrentAccountProfile current,
        CancellationToken cancellationToken = default) =>
        await ExecuteCoreAsync(
            targetProfileId,
            lockActiveRelationship: true,
            current,
            cancellationToken);

    private async Task<PatientAccessAuthorizationResult> ExecuteCoreAsync(
        EntityId targetProfileId,
        bool lockActiveRelationship,
        ResolvedCurrentAccountProfile? current,
        CancellationToken cancellationToken)
    {
        current ??= await currentAccountResolver.ResolveAsync(cancellationToken);

        if (targetProfileId == current.PrimaryProfile.Id)
        {
            return PatientAccessAuthorizationResult.Primary();
        }

        var lookup = lockActiveRelationship
            ? await repository.FindForPatientUpdateAsync(
                current.PrimaryProfile.Id,
                targetProfileId,
                cancellationToken)
            : await repository.FindAsync(
                current.PrimaryProfile.Id,
                targetProfileId,
                cancellationToken);
        if (lookup.ActiveRelationshipId is { } relationshipId)
        {
            return PatientAccessAuthorizationResult.Managed(relationshipId);
        }

        auditLogger.PatientAccessDenied(
            current.Account.Id,
            current.PrimaryProfile.Id,
            targetProfileId,
            lookup.TargetExists
                ? PatientAccessDenialCategory.NoActiveManagementRelationship
                : PatientAccessDenialCategory.TargetNotFound,
            clock.UtcNow);
        return PatientAccessAuthorizationResult.Denied();
    }
}

public enum PatientAccessReason
{
    Denied = 0,
    Primary = 1,
    Managed = 2
}

public sealed record PatientAccessAuthorizationResult
{
    private PatientAccessAuthorizationResult(
        PatientAccessReason reason,
        EntityId? relationshipId)
    {
        Reason = reason;
        RelationshipId = relationshipId;
    }

    public PatientAccessReason Reason { get; }

    public EntityId? RelationshipId { get; }

    public bool IsAuthorized => Reason != PatientAccessReason.Denied;

    public static PatientAccessAuthorizationResult Primary() =>
        new(PatientAccessReason.Primary, null);

    public static PatientAccessAuthorizationResult Managed(EntityId relationshipId) =>
        new(PatientAccessReason.Managed, relationshipId);

    public static PatientAccessAuthorizationResult Denied() =>
        new(PatientAccessReason.Denied, null);
}

public enum PatientAccessDenialCategory
{
    TargetNotFound = 0,
    NoActiveManagementRelationship = 1
}
