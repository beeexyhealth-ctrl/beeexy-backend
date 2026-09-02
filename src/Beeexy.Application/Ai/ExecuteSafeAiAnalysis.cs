using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Ai;

public sealed class ExecuteSafeAiAnalysis(
    ExecuteAiAnalysis executionPipeline,
    IAiSafetyValidator safetyValidator,
    IAiSafetyPersistence persistence,
    IAiSafetyTelemetry telemetry,
    AiSafetyProductContent productContent,
    IClock clock)
{
    public async Task<AiSafeAnalysisOutcome> ExecuteAsync(
        ExecuteSafeAiAnalysisCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Execution);
        var technical = await executionPipeline.ExecuteAsync(
            command.Execution,
            cancellationToken);
        if (!technical.RequiresSafetyValidation ||
            string.IsNullOrWhiteSpace(technical.StructurallyValidatedContent))
        {
            return new AiSafeAnalysisOutcome(
                technical.ExecutionId,
                technical.Kind,
                null,
                false,
                null,
                null,
                null,
                null,
                null);
        }

        var decision = safetyValidator.Validate(new AiSafetyValidationInput(
            command.Execution.WorkloadIdentifier,
            technical.StructurallyValidatedContent));
        var validatedAt = clock.UtcNow;
        if (decision.IsApproved)
        {
            var resultSchemaVersion = AiContractGuard.Identifier(
                $"{command.Execution.ExpectedResult.SchemaIdentifier}@" +
                command.Execution.ExpectedResult.Version,
                "resultSchemaVersion");
            var snapshot = AiResultSnapshot.Create(
                command.Execution.AnalysisRequestId,
                technical.ExecutionId,
                1,
                resultSchemaVersion,
                technical.StructurallyValidatedContent,
                validatedAt);
            var validation = AiSafetyValidation.CreateApproved(
                technical.ExecutionId,
                snapshot.Id,
                decision.PolicyVersion,
                validatedAt,
                productContent.DisclaimerVersion);
            persistence.AddApproved(snapshot, validation);
            await persistence.SaveChangesAsync(CancellationToken.None);
            telemetry.DecisionPersisted(validation);
            return new AiSafeAnalysisOutcome(
                technical.ExecutionId,
                technical.Kind,
                decision.Category,
                true,
                technical.StructurallyValidatedContent,
                productContent.Disclaimer,
                productContent.DisclaimerVersion,
                validation.Id,
                snapshot.Id);
        }

        var fallback = decision.UseCriticalFallback
            ? productContent.CriticalFallback
            : productContent.GenericFallback;
        var fallbackVersion = decision.UseCriticalFallback
            ? productContent.CriticalFallbackVersion
            : productContent.GenericFallbackVersion;
        var rejected = AiSafetyValidation.CreateRejected(
            technical.ExecutionId,
            decision.Category,
            decision.PolicyVersion,
            technical.StructurallyValidatedContent,
            validatedAt,
            fallbackVersion);
        persistence.AddRejected(rejected);
        await persistence.SaveChangesAsync(CancellationToken.None);
        telemetry.DecisionPersisted(rejected);
        return new AiSafeAnalysisOutcome(
            technical.ExecutionId,
            technical.Kind,
            decision.Category,
            false,
            fallback,
            productContent.Disclaimer,
            fallbackVersion,
            rejected.Id,
            null);
    }
}
