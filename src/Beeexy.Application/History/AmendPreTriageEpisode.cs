using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;

namespace Beeexy.Application.History;

public sealed class AmendPreTriageEpisode(
    IClock clock,
    CurrentAccountProfileResolver currentAccountResolver,
    AuthorizePatientAccess authorizePatientAccess,
    IPreTriageAmendmentRepository repository,
    IClinicalAmendmentAuditLogger auditLogger)
{
    public async Task<AmendPreTriageEpisodeResult> ExecuteAsync(
        AmendPreTriageEpisodeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var current = await currentAccountResolver.ResolveAsync(cancellationToken);
        PatientAccessAuthorizationResult? authorization = null;

        ClinicalAmendment? amendment;
        try
        {
            amendment = await repository.CreateLockedAsync(
                command.EpisodeId,
                async source =>
                {
                    authorization = await authorizePatientAccess
                        .ExecuteForPatientUpdateAsync(
                            source.PatientProfileId,
                            current,
                            cancellationToken);
                    if (!authorization.IsAuthorized)
                    {
                        throw new PatientProfileNotFoundException();
                    }

                    var request = Validate(command);
                    return ClinicalAmendment.CreateForRequest(
                        source.HistoryEvent,
                        current.Account.Id,
                        request.Reason,
                        ToPostgreSqlPrecision(clock.UtcNow),
                        request.IdempotencyKey);
                },
                cancellationToken);
        }
        catch (ClinicalAmendmentDuplicateException)
        {
            auditLogger.DuplicateRejected(
                current.Account.Id,
                command.EpisodeId,
                authorization?.Reason,
                clock.UtcNow);
            throw;
        }

        if (amendment is null)
        {
            throw new PatientProfileNotFoundException();
        }

        var accessReason = authorization?.Reason ?? throw new InvalidOperationException(
            "A persisted amendment is missing its authorization decision.");

        auditLogger.Created(
            current.Account.Id,
            amendment.ClinicalHistoryEventId,
            amendment.SourceId,
            amendment.Id,
            accessReason,
            amendment.CreatedAt);
        return new AmendPreTriageEpisodeResult(
            current.PrimaryProfile.BeeexyId.Value,
            amendment);
    }

    private static ValidatedAmendmentRequest Validate(
        AmendPreTriageEpisodeCommand command)
    {
        if (command.HasUnsupportedFields)
        {
            throw Invalid(
                "clinical_amendment.unsupported_fields",
                "The amendment request contains unsupported fields.");
        }

        if (!Guid.TryParse(command.IdempotencyKey, out var parsedKey) ||
            parsedKey == Guid.Empty)
        {
            throw Invalid(
                "clinical_amendment.invalid_idempotency_key",
                "A non-empty UUID idempotencyKey is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw Invalid(
                "clinical_amendment.invalid_reason",
                "A non-empty amendment reason is required.");
        }

        AmendmentReason reason;
        try
        {
            reason = AmendmentReason.Create(command.Reason);
        }
        catch (ArgumentException)
        {
            throw Invalid(
                "clinical_amendment.invalid_reason",
                "The amendment reason is invalid.");
        }

        return new ValidatedAmendmentRequest(
            EntityId.From(parsedKey),
            reason);
    }

    private static RequestValidationException Invalid(string code, string message) =>
        new(code, message);

    private static DateTimeOffset ToPostgreSqlPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);

    private sealed record ValidatedAmendmentRequest(
        EntityId IdempotencyKey,
        AmendmentReason Reason);
}

public sealed record AmendPreTriageEpisodeCommand(
    EntityId EpisodeId,
    string? IdempotencyKey,
    string? Reason,
    bool HasUnsupportedFields = false);

public sealed record AmendPreTriageEpisodeResult(
    string AuthorBeeexyId,
    ClinicalAmendment Amendment);

public sealed class ClinicalAmendmentDuplicateException : Exception;

public interface IPreTriageAmendmentRepository
{
    Task<ClinicalAmendment?> CreateLockedAsync(
        EntityId episodeId,
        Func<AmendablePreTriageSource, Task<ClinicalAmendment>> createAmendment,
        CancellationToken cancellationToken = default);
}

public sealed record AmendablePreTriageSource(
    EntityId PatientProfileId,
    ClinicalHistoryEvent HistoryEvent);

public interface IClinicalAmendmentAuditLogger
{
    void Created(
        EntityId actorAccountId,
        EntityId historyEventId,
        EntityId sourceEpisodeId,
        EntityId amendmentId,
        PatientAccessReason accessReason,
        DateTimeOffset createdAt);

    void DuplicateRejected(
        EntityId actorAccountId,
        EntityId sourceEpisodeId,
        PatientAccessReason? accessReason,
        DateTimeOffset rejectedAt);
}
