using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Ai;

public sealed class ExecuteAiAnalysis(
    IClock clock,
    IAiExecutionRepository repository,
    IAiPromptResolver promptResolver,
    IAiProvider provider,
    IAiStructuredResultValidator structuredResultValidator,
    IAiExecutionTelemetry telemetry)
{
    public async Task<AiExecutionOutcome> ExecuteAsync(
        ExecuteAiAnalysisCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var prompt = promptResolver.Resolve(command.Prompt, command.PreparedInput);
        var execution = AiExecution.CreatePending(
            command.AnalysisRequestId,
            clock.UtcNow);
        repository.Add(execution);
        await repository.SaveChangesAsync(cancellationToken);

        execution.Start(
            provider.ProviderIdentifier,
            provider.ModelIdentifier,
            prompt.Identity.PersistenceValue,
            clock.UtcNow);
        await repository.SaveChangesAsync(cancellationToken);
        telemetry.Started(execution);

        AiProviderResponse providerResponse;
        AiStructuralValidationResult validation;
        try
        {
            providerResponse = await provider.ExecuteAsync(
                new AiProviderRequest(
                    command.WorkloadIdentifier,
                    prompt.Identity,
                    prompt.SystemInstructions,
                    prompt.UserContent,
                    command.ExpectedResult,
                    command.CorrelationIdentifier),
                cancellationToken);
            if (providerResponse is null)
            {
                return await FailAsync(
                    execution,
                    AiExecutionFailureCategories.PermanentProviderFailure,
                    AiExecutionOutcomeKind.PermanentFailure);
            }

            validation = structuredResultValidator.Validate(
                command.ExpectedResult,
                providerResponse.StructuredContent);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await FailAsync(
                execution,
                AiExecutionFailureCategories.CallerCancellation,
                AiExecutionOutcomeKind.CallerCancelled);
        }
        catch (OperationCanceledException)
        {
            return await FailAsync(
                execution,
                AiExecutionFailureCategories.Timeout,
                AiExecutionOutcomeKind.Timeout);
        }
        catch (AiProviderException exception)
        {
            if (exception.Category == AiProviderFailureCategory.MalformedResponse)
            {
                execution.MarkRejected(clock.UtcNow);
                await repository.SaveChangesAsync(CancellationToken.None);
                telemetry.Completed(execution);
                return new AiExecutionOutcome(
                    execution.Id,
                    AiExecutionOutcomeKind.MalformedResult,
                    StructuralIssue: AiStructuralValidationIssue.InvalidStructure);
            }

            var (failureCategory, outcome) = exception.Category switch
            {
                AiProviderFailureCategory.Timeout => (
                    AiExecutionFailureCategories.Timeout,
                    AiExecutionOutcomeKind.Timeout),
                AiProviderFailureCategory.Transient => (
                    AiExecutionFailureCategories.TransientProviderFailure,
                    AiExecutionOutcomeKind.TransientFailure),
                AiProviderFailureCategory.ConfigurationUnavailable => (
                    AiExecutionFailureCategories.ConfigurationUnavailable,
                    AiExecutionOutcomeKind.ConfigurationUnavailable),
                _ => (
                    AiExecutionFailureCategories.PermanentProviderFailure,
                    AiExecutionOutcomeKind.PermanentFailure)
            };
            return await FailAsync(execution, failureCategory, outcome);
        }
        catch (Exception)
        {
            return await FailAsync(
                execution,
                AiExecutionFailureCategories.PermanentProviderFailure,
                AiExecutionOutcomeKind.PermanentFailure);
        }

        if (!validation.IsValid)
        {
            execution.MarkRejected(clock.UtcNow);
            await repository.SaveChangesAsync(CancellationToken.None);
            telemetry.Completed(execution);
            return new AiExecutionOutcome(
                execution.Id,
                AiExecutionOutcomeKind.MalformedResult,
                StructuralIssue: validation.Issue);
        }

        execution.MarkSucceeded(clock.UtcNow);
        await repository.SaveChangesAsync(CancellationToken.None);
        telemetry.Completed(execution);
        return new AiExecutionOutcome(
            execution.Id,
            AiExecutionOutcomeKind.StructurallyValid,
            providerResponse.StructuredContent);
    }

    private async Task<AiExecutionOutcome> FailAsync(
        AiExecution execution,
        string failureCategory,
        AiExecutionOutcomeKind outcome)
    {
        execution.MarkFailed(failureCategory, clock.UtcNow);
        await repository.SaveChangesAsync(CancellationToken.None);
        telemetry.Completed(execution);
        return new AiExecutionOutcome(execution.Id, outcome);
    }
}
