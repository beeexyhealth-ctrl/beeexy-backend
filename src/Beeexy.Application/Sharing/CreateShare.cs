using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;

namespace Beeexy.Application.Sharing;

public sealed class CreateShare(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    ShareLifetimePolicy lifetimePolicy,
    ShareUrlOptions urlOptions,
    IShareCapabilityService capabilityService,
    IShareCreationTransaction transaction)
{
    public const int MaximumItemCount = 100;

    public async Task<CreateShareResult> ExecuteAsync(
        CreateShareCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Items);
        ValidateCommand(command);
        var lifetime = lifetimePolicy.Resolve(command.LifetimeMinutes);
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        var patientId = current.PrimaryProfile.Id;
        var fingerprint = ShareRequestFingerprintCalculator.Calculate(
            patientId,
            command.Scope,
            lifetime,
            command.Items);

        await transaction.BeginAsync(patientId, command.IdempotencyKey, cancellationToken);
        var existing = await transaction.FindExistingAsync(
            patientId,
            command.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            EnsureFingerprintMatches(existing.Grant, fingerprint);
            await transaction.CommitAsync(cancellationToken);
            return new CreateShareResult(
                ToSummary(existing, CurrentInstant()),
                NewlyCreated: false,
                Capability: null,
                ShareUrl: null);
        }

        if (!await transaction.AllItemsBelongToPatientAsync(
                patientId,
                command.Items,
                cancellationToken))
        {
            throw new ShareItemNotFoundException();
        }

        var createdAt = CurrentInstant();
        var generated = capabilityService.Generate();
        var grant = ShareGrant.Create(
            patientId,
            current.Account.Id,
            command.IdempotencyKey,
            fingerprint,
            command.Scope,
            generated.Hash,
            createdAt,
            createdAt.Add(lifetime));
        var items = command.Items.Select(item => ShareGrantItem.Create(
            grant,
            item.ResourceType,
            item.ResourceId,
            createdAt)).ToArray();
        var createdEvent = ShareAccessEvent.Create(
            grant,
            ShareAccessEventType.ShareCreated,
            ShareAccessOutcome.Succeeded,
            createdAt);

        transaction.Add(grant, items, createdEvent);
        await transaction.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreateShareResult(
            ToSummary(new ShareCreationState(grant, items.Length), createdAt),
            NewlyCreated: true,
            generated.Value,
            urlOptions.Build(generated.Value));
    }

    private static void ValidateCommand(CreateShareCommand command)
    {
        if (command.IdempotencyKey.Value == Guid.Empty)
        {
            throw new RequestValidationException(
                "sharing.idempotency_key_required",
                "A non-empty idempotency key is required.");
        }

        if (!Enum.IsDefined(command.Scope))
        {
            throw new RequestValidationException(
                "sharing.scope_invalid",
                "The share scope is invalid.");
        }

        if (command.Scope is ShareScope.Case or ShareScope.Visit)
        {
            throw new RequestValidationException(
                "sharing.scope_unavailable",
                "The requested share scope is not available.");
        }

        if (command.Items.Count > MaximumItemCount)
        {
            throw new RequestValidationException(
                "sharing.items_invalid",
                $"A share cannot contain more than {MaximumItemCount} explicit items.");
        }

        if (command.Scope is ShareScope.PreTriage or ShareScope.SpecificRecords &&
            command.Items.Count == 0)
        {
            throw new RequestValidationException(
                "sharing.items_required",
                "The selected share scope requires at least one explicit item.");
        }

        if (command.Scope == ShareScope.FullProfile && command.Items.Count != 0)
        {
            throw new RequestValidationException(
                "sharing.items_not_allowed",
                "FullProfile does not accept explicit items.");
        }

        if (command.Scope == ShareScope.PreTriage && command.Items.Any(item =>
                item.ResourceType.Value != SupportedShareResourceTypes.PreTriageEpisode))
        {
            throw new RequestValidationException(
                "sharing.item_type_invalid",
                "PreTriage accepts only completed Pre-Triage episode items.");
        }

        if (command.Items.Any(item =>
                item is null ||
                item.ResourceId.Value == Guid.Empty ||
                !SupportedShareResourceTypes.Contains(item.ResourceType)))
        {
            throw new RequestValidationException(
                "sharing.item_type_invalid",
                "Every share item must use a supported resource type and non-empty identifier.");
        }

        if (command.Items.Distinct().Count() != command.Items.Count)
        {
            throw new RequestValidationException(
                "sharing.items_duplicate",
                "A share cannot contain duplicate item references.");
        }
    }

    private static void EnsureFingerprintMatches(
        ShareGrant existing,
        ShareRequestFingerprint fingerprint)
    {
        if (existing.RequestFingerprint != fingerprint)
        {
            throw new ShareIdempotencyConflictException();
        }
    }

    private DateTimeOffset CurrentInstant()
    {
        var utc = clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }

    internal static ShareSummary ToSummary(ShareCreationState state, DateTimeOffset now) => new(
        state.Grant.Id,
        state.Grant.Scope,
        state.Grant.GetStatus(now),
        state.Grant.CreatedAt,
        state.Grant.ExpiresAt,
        state.Grant.RevokedAt,
        state.ItemCount);
}
