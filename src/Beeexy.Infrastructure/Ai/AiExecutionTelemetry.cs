using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Ai;

internal sealed class AiExecutionTelemetry(ILogger<AiExecutionTelemetry> logger)
    : IAiExecutionTelemetry
{
    public void Started(AiExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        logger.LogInformation(
            "AI execution {ExecutionId} started with provider {ProviderIdentifier}, model {ModelIdentifier}, and prompt version {PromptVersion}.",
            execution.Id.Value,
            execution.ProviderIdentifier,
            execution.ModelIdentifier,
            execution.PromptVersion);
    }

    public void Completed(AiExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        logger.LogInformation(
            "AI execution {ExecutionId} completed with status {Status}, latency {LatencyMilliseconds}, and safe failure category {FailureCategory}.",
            execution.Id.Value,
            execution.Status,
            execution.LatencyMilliseconds,
            execution.SanitizedFailureCategory);
    }
}
