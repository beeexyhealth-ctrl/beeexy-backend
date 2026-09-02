using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Microsoft.Extensions.Logging;

namespace Beeexy.Infrastructure.Ai;

public sealed class AiSafetyTelemetry(ILogger<AiSafetyTelemetry> logger) : IAiSafetyTelemetry
{
    public void DecisionPersisted(AiSafetyValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        logger.LogInformation(
            "AI safety validation {SafetyValidationId} for execution {ExecutionId} " +
            "persisted category {Category}, policy {PolicyVersion}, and provider-output " +
            "display eligibility {DisplayEligible}.",
            validation.Id.Value,
            validation.ExecutionId.Value,
            validation.Category,
            validation.PolicyVersion,
            validation.DisplayEligible);
    }
}
