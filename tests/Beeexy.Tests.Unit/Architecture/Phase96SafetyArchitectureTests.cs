using Beeexy.Api.Care;
using Beeexy.Application.Care;
using Beeexy.Infrastructure.Care;

namespace Beeexy.Tests.Unit.Architecture;

[Trait("Category", "Phase96")]
[Trait("Category", "Phase96Architecture")]
public sealed class Phase96SafetyArchitectureTests
{
    [Fact]
    public void HistoryDependsOnAuthorizationExactBatchContentAndReadOnlyRepository()
    {
        var dependencies = typeof(ListSymptomCheckIns).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Contains(typeof(ISymptomDiaryEpisodeReadRepository), dependencies);
        Assert.Contains(typeof(ISymptomCheckInReadRepository), dependencies);
        Assert.Contains(typeof(ISymptomDiaryExactContentBatchProvider), dependencies);
        Assert.Contains(typeof(ISymptomDiaryHistoryCursorCodec), dependencies);
        Assert.DoesNotContain(dependencies, type =>
            type.Namespace?.Contains("Ai", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("History", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("Interoperability", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("Scheduling", StringComparison.Ordinal) == true ||
            type.Name.Contains("Notification", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PublicHistoryShapeIsNeutralAndContainsNoPersistenceSecretsOrDerivedFields()
    {
        var names = typeof(SymptomCheckInHistoryPageResponse).GetProperties()
            .Concat(typeof(SymptomCheckInResponse).GetProperties())
            .Concat(typeof(SymptomDiaryAcceptedAnswerResponse).GetProperties())
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["Items", "NextCursor"],
            typeof(SymptomCheckInHistoryPageResponse).GetProperties()
                .Select(property => property.Name));
        Assert.DoesNotContain(names, IsForbidden);
    }

    [Fact]
    public void Phase96AddsOnlyListAndNoMutationOrClinicalProcessingService()
    {
        Assert.Equal("/api/v1/pre-triage/episodes/{episodeId:guid}/check-ins",
            SymptomDiaryEndpointExtensions.CheckInRoute);
        var types = typeof(ListSymptomCheckIns).Assembly.GetTypes()
            .Where(type => type.Namespace == "Beeexy.Application.Care")
            .ToArray();

        Assert.DoesNotContain(types, type =>
            type.Name is "UpdateSymptomCheckIn" or "DeleteSymptomCheckIn" ||
            type.Name.Contains("Trend", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Comparison", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("WarningMatcher", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Severity", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Recommendation", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Escalation", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Reminder", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(SymptomCheckInReadRepository).GetMethods(), method =>
            method.Name.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsForbidden(string name) =>
        name.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Idempotency", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("RequestHash", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SourceReference", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Imported", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CanonicalJson", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Trend", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Improving", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Worsening", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Severity", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Risk", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Urgency", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Diagnosis", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Recommendation", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Escalation", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Reminder", StringComparison.OrdinalIgnoreCase);
}
