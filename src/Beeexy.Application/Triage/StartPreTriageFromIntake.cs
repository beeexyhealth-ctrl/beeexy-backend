using System.Security.Cryptography;
using System.Text;
using Beeexy.Application.Common;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class StartPreTriageFromIntake(
    IClock clock,
    InterpretPreTriageIntake interpretPreTriageIntake,
    StartPreTriage startPreTriage,
    SubmitTriageAnswers submitTriageAnswers,
    ReplayPreTriageIntake replayPreTriageIntake,
    IPreTriageIntakeOrchestrationTransaction transaction)
{
    public async Task<StartPreTriageFromIntakeResult> ExecuteAsync(
        StartPreTriageFromIntakeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateIdempotencyInput(command);
        var operationKeyHash = Hash(
            $"beeexy.pre-triage.intake.operation.v1\n{command.CallerScope}\n" +
            command.IdempotencyKey);
        var reservationAliasHash = command.RequiresAnonymousBootstrapReservation
            ? Hash(
                "beeexy.pre-triage.intake.anonymous-bootstrap.v1\n" +
                command.IdempotencyKey)
            : null;
        var requestFingerprint = Hash(
            $"beeexy.pre-triage.intake.request.v1\n{command.Text!.Trim()}");

        var transactionResult = await transaction.ExecuteAsync(
            operationKeyHash,
            reservationAliasHash,
            requestFingerprint,
            async transactionCancellationToken =>
            {
                var interpretation = await interpretPreTriageIntake.ExecuteAsync(
                    new InterpretPreTriageIntakeCommand(
                        command.Text,
                        command.UnsupportedFields),
                    transactionCancellationToken);
                if (interpretation.Resolution != PreTriageIntakeResolution.Resolved ||
                    interpretation.Pathway is null)
                {
                    return PreTriageIntakeTransactionCommit<StartPreTriageFromIntakeResult>
                        .WithoutDurableResult(new StartPreTriageFromIntakeResult(
                            interpretation.Resolution,
                            interpretation.CandidatePathways,
                            null,
                            null));
                }

                var session = await startPreTriage.ExecuteForOrchestrationAsync(
                    new StartPreTriageCommand(
                        interpretation.Pathway.Value,
                        null,
                        command.CallerMode,
                        []),
                    transactionCancellationToken);
                var initialAnswers = await submitTriageAnswers
                    .ApplyInitialCandidatesForOrchestrationAsync(
                    new ApplyInitialTriageCandidatesCommand(
                        session.SessionId,
                        command.CallerMode,
                        session.AnonymousCapability,
                        interpretation.CandidateValues),
                    transactionCancellationToken);
                var result = new StartPreTriageFromIntakeResult(
                    PreTriageIntakeResolution.Resolved,
                    [],
                    session,
                    initialAnswers);
                return PreTriageIntakeTransactionCommit<StartPreTriageFromIntakeResult>
                    .WithDurableResult(
                        result,
                        session.SessionId,
                        initialAnswers.AcceptedAnswerCodes,
                        ToPostgreSqlPrecision(session.CreatedAt),
                        ToPostgreSqlPrecision(clock.UtcNow));
            },
            cancellationToken);

        if (transactionResult.Replay is not null)
        {
            return await replayPreTriageIntake.ExecuteAsync(
                new ReplayPreTriageIntakeQuery(
                    transactionResult.Replay.SessionId,
                    transactionResult.Replay.InitialAnswerCodes,
                    command.CallerMode,
                    command.AnonymousCapability),
                cancellationToken);
        }

        var resolved = transactionResult.Result!;
        if (resolved.Resolution != PreTriageIntakeResolution.Resolved)
        {
            return resolved;
        }

        startPreTriage.AuditCreated(resolved.Session!, command.CallerMode);
        submitTriageAnswers.AuditInitialAnswers(resolved.InitialAnswers!);
        return resolved;
    }

    private static void ValidateIdempotencyInput(StartPreTriageFromIntakeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) ||
            string.IsNullOrWhiteSpace(command.CallerScope))
        {
            throw new RequestValidationException(
                "pre_triage.idempotency_key_invalid",
                "A valid Idempotency-Key header is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Text) ||
            command.Text.Length > InterpretPreTriageIntake.MaximumTextLength ||
            command.UnsupportedFields.Count > 0)
        {
            throw new RequestValidationException(
                "pre_triage.intake_interpretation_invalid",
                "A bounded first-message text value is required without unsupported fields.");
        }
    }

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static DateTimeOffset ToPostgreSqlPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);
}

public sealed record StartPreTriageFromIntakeCommand(
    string? Text,
    PreTriageCallerMode CallerMode,
    IReadOnlyCollection<string> UnsupportedFields,
    string IdempotencyKey,
    string CallerScope,
    string? AnonymousCapability,
    bool RequiresAnonymousBootstrapReservation);

public sealed record StartPreTriageFromIntakeResult(
    PreTriageIntakeResolution Resolution,
    IReadOnlyList<ClinicalPathwayCode> CandidatePathways,
    StartPreTriageResult? Session,
    SubmitTriageAnswersResult? InitialAnswers);

public sealed record PreTriageIntakeReplayReference(
    EntityId SessionId,
    IReadOnlyList<string> InitialAnswerCodes);

public sealed record PreTriageIntakeTransactionResult<TResult>(
    TResult? Result,
    PreTriageIntakeReplayReference? Replay);

public sealed record PreTriageIntakeTransactionCommit<TResult>(
    TResult Result,
    EntityId? SessionId,
    IReadOnlyList<QuestionCode> InitialAnswerCodes,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static PreTriageIntakeTransactionCommit<TResult> WithoutDurableResult(
        TResult result) => new(result, null, [], null, null);

    public static PreTriageIntakeTransactionCommit<TResult> WithDurableResult(
        TResult result,
        EntityId sessionId,
        IReadOnlyList<QuestionCode> initialAnswerCodes,
        DateTimeOffset createdAt,
        DateTimeOffset completedAt) => new(
            result,
            sessionId,
            initialAnswerCodes,
            createdAt,
            completedAt);
}

public interface IPreTriageIntakeOrchestrationTransaction
{
    Task<PreTriageIntakeTransactionResult<TResult>> ExecuteAsync<TResult>(
        string operationKeyHash,
        string? reservationAliasHash,
        string requestFingerprint,
        Func<CancellationToken, Task<PreTriageIntakeTransactionCommit<TResult>>> operation,
        CancellationToken cancellationToken = default);
}

public sealed class PreTriageIntakeIdempotencyConflictException : Exception
{
    public PreTriageIntakeIdempotencyConflictException()
        : base("The pre-triage intake idempotency key belongs to a different request.")
    {
    }
}
