using Beeexy.Api.Care;
using Beeexy.Application.Care;

namespace Beeexy.Tests.Unit.Architecture;

[Trait("Category", "Phase95")]
[Trait("Category", "Phase95Architecture")]
public sealed class Phase95SafetyArchitectureTests
{
    [Fact]
    public void CommandAcceptsNoClientAuthorityOrClinicalDerivedFields()
    {
        Assert.Equal(
            ["EpisodeId", "PackageVersionId", "IdempotencyKey", "Answers", "AnswersProvided", "HasUnsupportedFields"],
            typeof(RecordSymptomCheckInCommand).GetProperties().Select(value => value.Name));
        Assert.DoesNotContain(
            typeof(RecordSymptomCheckInCommand).GetProperties(),
            property => IsForbidden(property.Name) ||
                property.Name.Contains("Patient", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Pathway", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Time", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreationUsesTrustedEpisodeExactPackageStructuralValidatorAndTransaction()
    {
        var dependencies = typeof(RecordSymptomCheckIn).GetConstructors().Single()
            .GetParameters().Select(value => value.ParameterType).ToArray();

        Assert.Contains(typeof(ISymptomDiaryEpisodeReadRepository), dependencies);
        Assert.Contains(typeof(ISymptomDiaryContentProvider), dependencies);
        Assert.Contains(typeof(SymptomDiaryAnswerStructureValidator), dependencies);
        Assert.Contains(typeof(ISymptomCheckInTransaction), dependencies);
        Assert.DoesNotContain(dependencies, type =>
            type.Namespace?.Contains("Ai", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("History", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("Interoperability", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("Scheduling", StringComparison.Ordinal) == true ||
            type.Name.Contains("Notification", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Outbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApiAddsOnlyPostCreationShapeAndNoHistoryOrMutationSurface()
    {
        Assert.Equal(
            "/api/v1/pre-triage/episodes/{episodeId:guid}/check-ins",
            SymptomDiaryEndpointExtensions.CheckInRoute);
        var applicationTypes = typeof(RecordSymptomCheckIn).Assembly.GetTypes()
            .Where(type => type.Namespace == "Beeexy.Application.Care")
            .ToArray();
        Assert.DoesNotContain(applicationTypes, type =>
            type.Name is "ListSymptomCheckIns" or "UpdateSymptomCheckIn" or
                "DeleteSymptomCheckIn" ||
            type.Name.Contains("WarningMatcher", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("SymptomTrend", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Recommendation", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Reminder", StringComparison.OrdinalIgnoreCase));

        var responseProperties = typeof(SymptomCheckInResponse).GetProperties()
            .Concat(typeof(SymptomDiaryAcceptedAnswerResponse).GetProperties())
            .Select(value => value.Name);
        Assert.DoesNotContain(responseProperties, IsForbidden);
    }

    private static bool IsForbidden(string name) =>
        name.Contains("Detected", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("MatchedWarning", StringComparison.OrdinalIgnoreCase) ||
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
        name.Contains("Improving", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Worsening", StringComparison.OrdinalIgnoreCase);
}
