using Beeexy.Application.Care;
using Beeexy.Infrastructure.Care;

namespace Beeexy.Tests.Unit.Architecture;

[Trait("Category", "Phase92Architecture")]
[Trait("Category", "Phase92")]
public sealed class Phase92SafetyArchitectureTests
{
    [Fact]
    public void CarePackageInfrastructure_HasNoForbiddenProductOrClinicalService()
    {
        var careTypes = typeof(SymptomDiaryContentProvider).Assembly.GetTypes()
            .Where(type => type.Namespace == "Beeexy.Infrastructure.Care")
            .Where(type => !type.IsNested)
            .ToArray();

        Assert.Contains(careTypes, type => type == typeof(SymptomDiaryContentImporter));
        Assert.Contains(careTypes, type => type == typeof(SymptomDiaryContentProvider));
        Assert.DoesNotContain(careTypes, type => IsForbidden(type.Name));

        var dependencies = careTypes
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.DoesNotContain(dependencies, type =>
            type.Namespace?.StartsWith("Beeexy.Application.Ai", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Beeexy.Infrastructure.Ai", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Beeexy.Application.Interoperability",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Beeexy.Infrastructure.Interoperability",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Beeexy.Application.History", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Beeexy.Infrastructure.History", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void PublicCareContracts_ExposeOnlyPackageImportAndLookupBoundaries()
    {
        var methods = new[]
        {
            typeof(ISymptomDiaryContentImporter),
            typeof(ISymptomDiaryContentProvider)
        }.SelectMany(type => type.GetMethods()).ToArray();

        Assert.Equal(
            ["GetActivePackageAsync", "GetExactPackageAsync", "ImportAsync"],
            methods.Select(value => value.Name).OrderBy(value => value).ToArray());
        Assert.DoesNotContain(methods, method =>
            method.GetParameters().Any(parameter =>
                IsForbidden(parameter.ParameterType.FullName ?? string.Empty)) ||
            IsForbidden(method.ReturnType.FullName ?? string.Empty));
    }

    private static bool IsForbidden(string value) =>
        value.Contains("Evaluator", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Matcher", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Recommendation", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Reminder", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Notification", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("ArtificialIntelligence", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Fhir", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("ClinicalHistory", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("CheckIn", StringComparison.OrdinalIgnoreCase);
}
