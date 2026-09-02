using Beeexy.Application.Ai;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;
using Beeexy.Infrastructure.Ai;
using Microsoft.Extensions.Logging;

namespace Beeexy.Tests.Unit.Ai;

[Trait("Category", "Phase103Security")]
[Trait("Category", "Phase108")]
public sealed class AiSafetyPrivacyTests
{
    [Fact]
    public void RejectedAuditOutput_IsNotLoggedBySafetyTelemetry()
    {
        const string raw = "private-health-output-secret-103";
        var logger = new CapturingLogger<AiSafetyTelemetry>();
        var telemetry = new AiSafetyTelemetry(logger);
        var validation = AiSafetyValidation.CreateRejected(
            EntityId.New(),
            AiSafetyCategory.Diagnosis,
            "ai-safety-policy-v1",
            raw,
            Utc(),
            "ai-rejection-fallback-v1");

        telemetry.DecisionPersisted(validation);

        var message = Assert.Single(logger.Messages);
        Assert.Contains(validation.Id.Value.ToString(), message, StringComparison.Ordinal);
        Assert.Contains(AiSafetyCategory.Diagnosis.ToString(), message,
            StringComparison.Ordinal);
        Assert.Contains("ai-safety-policy-v1", message, StringComparison.Ordinal);
        Assert.DoesNotContain(raw, message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-health", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TechnicalExecutionMetadata_HasNoPromptPayloadSecretOrRejectedOutputField()
    {
        var names = typeof(AiExecution).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(names, name =>
            name.Contains("Payload", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PromptText", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AuditOutput", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SafetyReasonMetadata_IsFiniteAndContainsNoFreeFormContent()
    {
        Assert.True(typeof(AiSafetyReasonCode).IsEnum);
        Assert.All(Enum.GetNames<AiSafetyReasonCode>(), name =>
            Assert.DoesNotContain("Text", name, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(AiSafetyDecision).GetProperties(), property =>
            property.Name.Contains("ReasonText", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private static DateTimeOffset Utc() =>
        new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
}
