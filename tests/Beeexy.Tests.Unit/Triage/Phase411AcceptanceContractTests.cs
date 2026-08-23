using Beeexy.Api.Triage;
using Beeexy.Application.Triage;

namespace Beeexy.Tests.Unit.Triage;

public sealed class Phase411AcceptanceContractTests
{
    [Fact]
    public void NeutralResultAndProjectionContracts_UseExactAllowListedShapes()
    {
        Assert.Equal(
            [
                "AdditionalSymptoms",
                "ClinicalContent",
                "CompletedAt",
                "Duration",
                "EpisodeId",
                "Intensity",
                "Package",
                "PrimarySymptom",
                "Questionnaire",
                "SessionId"
            ],
            PropertyNames(typeof(NeutralPreTriageResultResponse)));
        Assert.Equal(
            ["Event", "IsNewlyProjected"],
            PropertyNames(typeof(PreTriageHistoryProjectionOutcome)));
    }

    [Fact]
    public void ExecutableTriageLayers_ContainNoVendorOrFhirSpecificTypes()
    {
        var assemblies = new[]
        {
            typeof(PreTriageHistoryProjectionOutcome).Assembly,
            typeof(Beeexy.Domain.Triage.PreTriageSession).Assembly,
            typeof(Beeexy.Infrastructure.Triage.PreTriageCompletionRepository).Assembly,
            typeof(PreTriageEndpointExtensions).Assembly
        };
        var forbidden = new[]
        {
            "OpenAI", "Anthropic", "Gemini", "VertexAI", "Bedrock", "Azure.AI",
            "FHIR", "HL7"
        };
        var triageTypeNames = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace?.EndsWith(".Triage", StringComparison.Ordinal) == true)
            .Select(type => type.FullName!)
            .ToArray();

        Assert.NotEmpty(triageTypeNames);
        Assert.All(triageTypeNames, typeName => Assert.DoesNotContain(
            forbidden,
            value => typeName.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    private static string[] PropertyNames(Type type) => type
        .GetProperties()
        .Select(property => property.Name)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
}
