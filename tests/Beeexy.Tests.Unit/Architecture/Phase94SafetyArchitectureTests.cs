using Beeexy.Api.Care;
using Beeexy.Application.Care;
using Beeexy.Infrastructure.Care;

namespace Beeexy.Tests.Unit.Architecture;

[Trait("Category", "Phase94")]
[Trait("Category", "Phase94Architecture")]
public sealed class Phase94SafetyArchitectureTests
{
    [Fact]
    public void QueryAcceptsOnlyEpisodeIdentityAndUsesEpisodeRepositoryPlusActiveProvider()
    {
        var execute = typeof(GetSymptomDiaryContent).GetMethod("ExecuteAsync");
        Assert.NotNull(execute);
        Assert.Equal(
            ["episodeId", "cancellationToken"],
            execute.GetParameters().Select(value => value.Name));

        var dependencies = typeof(GetSymptomDiaryContent).GetConstructors().Single()
            .GetParameters()
            .Select(value => value.ParameterType)
            .ToArray();
        Assert.Contains(typeof(ISymptomDiaryEpisodeReadRepository), dependencies);
        Assert.Contains(typeof(ISymptomDiaryContentProvider), dependencies);
        Assert.DoesNotContain(dependencies, type =>
            type.Namespace?.Contains("Ai", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("History", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("Interoperability", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("Scheduling", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ApiAddsOnlyTheReviewedContentReadSurfaceWithNeutralResponseFields()
    {
        Assert.Equal(
            "/api/v1/pre-triage/episodes/{episodeId:guid}/symptom-diary-content",
            SymptomDiaryEndpointExtensions.ContentRoute);

        var apiTypes = typeof(SymptomDiaryEndpointExtensions).Assembly.GetTypes();
        Assert.DoesNotContain(apiTypes, type =>
            type.Name.Contains("CareGuide", StringComparison.OrdinalIgnoreCase));

        var responseProperties = typeof(SymptomDiaryContentResponse)
            .GetProperties()
            .Concat(typeof(SymptomDiaryContentProvenanceResponse).GetProperties())
            .Concat(typeof(SymptomDiaryQuestionResponse).GetProperties())
            .Concat(typeof(SymptomDiaryWarningSignResponse).GetProperties())
            .Select(value => value.Name)
            .ToArray();
        Assert.DoesNotContain(responseProperties, IsForbidden);
    }

    [Fact]
    public void Phase94AddsNoLaterPhaseOrCrossSystemService()
    {
        var applicationTypes = typeof(GetSymptomDiaryContent).Assembly.GetTypes();
        Assert.Contains(applicationTypes, type => type == typeof(RecordSymptomCheckIn));
        Assert.DoesNotContain(applicationTypes, type =>
            type.Name is "ListSymptomCheckIns" ||
            type.Name.Contains("WarningMatcher", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("SymptomTrend", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("SymptomRecommendation", StringComparison.OrdinalIgnoreCase));

        var infrastructureDependencies = typeof(SymptomDiaryEpisodeReadRepository).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "Beeexy.Infrastructure.Care")
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.DoesNotContain(infrastructureDependencies, type =>
            type.Namespace?.StartsWith("Beeexy.Infrastructure.Ai", StringComparison.Ordinal) ==
                true ||
            type.Namespace?.StartsWith(
                "Beeexy.Infrastructure.Interoperability",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Beeexy.Infrastructure.History", StringComparison.Ordinal) ==
                true ||
            type.Name.Contains("Notification", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Outbox", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsForbidden(string name) =>
        name.Contains("Detected", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Triggered", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Severity", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Trend", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Risk", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Urgency", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Diagnosis", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Disposition", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Recommendation", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Escalation", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Reminder", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("NextAction", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SubmittedAnswer", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PatientAnswer", StringComparison.OrdinalIgnoreCase);
}
