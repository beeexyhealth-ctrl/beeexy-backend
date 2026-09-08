using Beeexy.Api.Middleware;
using Beeexy.Application.Care;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;

namespace Beeexy.Tests.Unit.Architecture;

[Trait("Category", "Phase93")]
[Trait("Category", "Phase93Architecture")]
public sealed class Phase93SafetyArchitectureTests
{
    [Fact]
    public void SourcePackages_UseOnlyNeutralStaticDisplayContracts()
    {
        var packageContractTypes = new[]
        {
            typeof(SymptomDiaryPackageDefinition),
            typeof(SymptomDiaryQuestionDefinition),
            typeof(SymptomDiaryQuestionOptionDefinition),
            typeof(SymptomWarningSignDefinition)
        };
        var publicMembers = packageContractTypes
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(publicMembers, IsForbiddenConcept);
        Assert.Empty(typeof(AndreaSymptomDiaryPackages).GetConstructors());
    }

    [Fact]
    public void Phase93_AddsNoCheckInEndpointOrCrossPhaseIntegration()
    {
        var apiTypes = typeof(CorrelationIdMiddleware).Assembly.GetTypes();
        Assert.DoesNotContain(apiTypes, type =>
            type.Name.Contains("CheckInEndpoint", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("CareGuide", StringComparison.OrdinalIgnoreCase));

        var releaseDependencies = typeof(AndreaSymptomDiaryPackages).GetMethods()
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.DoesNotContain(releaseDependencies, type =>
            type.Namespace?.StartsWith("Beeexy.Application.Ai", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Beeexy.Infrastructure.Ai", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("Interoperability", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("History", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("Scheduling", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ExistingPhase4RegistrySnapshot_IsUnchangedWhileAllAndreaPackagesAreActive()
    {
        Assert.Equal(
            [
                ClinicalPathways.Headache,
                ClinicalPathways.AbdominalPain,
                ClinicalPathways.ChestPain,
                ClinicalPathways.Fever,
                ClinicalPathways.OtherSymptoms
            ],
            ClinicalPathways.Supported);
        Assert.All(AndreaSymptomDiaryPackages.CreateAll(), package =>
            Assert.Equal(AndreaSymptomDiaryPackages.ApprovalEffectiveAt, package.ActivatedAt));
    }

    private static bool IsForbiddenConcept(string value) =>
        value.Contains("Condition", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Threshold", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Score", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Risk", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Urgency", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Disposition", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Recommendation", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Escalation", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Reminder", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("WarningMatch", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Assessment", StringComparison.OrdinalIgnoreCase);
}
