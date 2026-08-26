using Beeexy.Domain.Common;

namespace Beeexy.Domain.Triage;

public sealed class PreTriageIntakeIdempotencyRecord
{
    public const int HashMaximumLength = 71;

    private PreTriageIntakeIdempotencyRecord()
    {
        OperationKeyHash = null!;
        RequestFingerprint = null!;
        InitialAnswerCodes = [];
    }

    private PreTriageIntakeIdempotencyRecord(
        EntityId id,
        string operationKeyHash,
        string? reservationAliasHash,
        string requestFingerprint,
        EntityId sessionId,
        string[] initialAnswerCodes,
        DateTimeOffset createdAt,
        DateTimeOffset completedAt)
    {
        Id = id;
        OperationKeyHash = operationKeyHash;
        ReservationAliasHash = reservationAliasHash;
        RequestFingerprint = requestFingerprint;
        SessionId = sessionId;
        InitialAnswerCodes = initialAnswerCodes;
        CreatedAt = createdAt;
        CompletedAt = completedAt;
    }

    public EntityId Id { get; private set; }

    public string OperationKeyHash { get; private set; }

    public string? ReservationAliasHash { get; private set; }

    public string RequestFingerprint { get; private set; }

    public EntityId SessionId { get; private set; }

    public string[] InitialAnswerCodes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    public static PreTriageIntakeIdempotencyRecord CreateCompleted(
        string operationKeyHash,
        string? reservationAliasHash,
        string requestFingerprint,
        EntityId sessionId,
        IReadOnlyCollection<QuestionCode> initialAnswerCodes,
        DateTimeOffset createdAt,
        DateTimeOffset completedAt,
        EntityId? id = null)
    {
        ValidateHash(operationKeyHash, nameof(operationKeyHash));
        if (reservationAliasHash is not null)
        {
            ValidateHash(reservationAliasHash, nameof(reservationAliasHash));
        }
        ValidateHash(requestFingerprint, nameof(requestFingerprint));
        EnsureNonEmpty(sessionId, nameof(sessionId));
        ArgumentNullException.ThrowIfNull(initialAnswerCodes);
        InstantGuard.EnsureUtc(createdAt, nameof(createdAt));
        InstantGuard.EnsureUtc(completedAt, nameof(completedAt));
        if (completedAt < createdAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAt),
                "Idempotency completion cannot precede creation.");
        }

        var codes = initialAnswerCodes.Select(value => value.Value).ToArray();
        if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Length)
        {
            throw new ArgumentException(
                "Initial answer codes must be unique.",
                nameof(initialAnswerCodes));
        }

        return new PreTriageIntakeIdempotencyRecord(
            id ?? EntityId.New(),
            operationKeyHash,
            reservationAliasHash,
            requestFingerprint,
            sessionId,
            codes,
            createdAt,
            completedAt);
    }

    private static void ValidateHash(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > HashMaximumLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void EnsureNonEmpty(EntityId id, string parameterName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", parameterName);
        }
    }
}
