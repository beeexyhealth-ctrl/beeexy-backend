using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class StartPreTriageFromIntake(
    InterpretPreTriageIntake interpretPreTriageIntake,
    StartPreTriage startPreTriage,
    SubmitTriageAnswers submitTriageAnswers,
    IPreTriageIntakeOrchestrationTransaction transaction)
{
    public async Task<StartPreTriageFromIntakeResult> ExecuteAsync(
        StartPreTriageFromIntakeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var interpretation = await interpretPreTriageIntake.ExecuteAsync(
            new InterpretPreTriageIntakeCommand(
                command.Text,
                command.UnsupportedFields),
            cancellationToken);
        if (interpretation.Resolution != PreTriageIntakeResolution.Resolved ||
            interpretation.Pathway is null)
        {
            return new StartPreTriageFromIntakeResult(
                interpretation.Resolution,
                interpretation.CandidatePathways,
                null,
                null);
        }

        var resolved = await transaction.ExecuteAsync(
            async transactionCancellationToken =>
            {
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
                return new StartPreTriageFromIntakeResult(
                    PreTriageIntakeResolution.Resolved,
                    [],
                    session,
                    initialAnswers);
            },
            cancellationToken);
        startPreTriage.AuditCreated(resolved.Session!, command.CallerMode);
        submitTriageAnswers.AuditInitialAnswers(resolved.InitialAnswers!);
        return resolved;
    }
}

public sealed record StartPreTriageFromIntakeCommand(
    string? Text,
    PreTriageCallerMode CallerMode,
    IReadOnlyCollection<string> UnsupportedFields);

public sealed record StartPreTriageFromIntakeResult(
    PreTriageIntakeResolution Resolution,
    IReadOnlyList<ClinicalPathwayCode> CandidatePathways,
    StartPreTriageResult? Session,
    SubmitTriageAnswersResult? InitialAnswers);

public interface IPreTriageIntakeOrchestrationTransaction
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
