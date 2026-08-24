using Beeexy.Api.History;
using Beeexy.Application.History;
using Beeexy.Domain.History;

namespace Beeexy.Tests.Unit.History;

public sealed class Phase56AcceptanceContractTests
{
    [Fact]
    public void PublicHistoryContracts_ExposeOnlyTheApprovedPhase5Shape()
    {
        Assert.Equal(
            ["Items", "NextCursor"],
            PropertyNames(typeof(ClinicalHistoryPageResponse)));
        Assert.Equal(
            ["EventId", "EventType", "OccurredAt", "RecordedAt", "Source"],
            PropertyNames(typeof(ClinicalHistoryItemResponse)));
        Assert.Equal(
            [
                "Amendments",
                "EventId",
                "EventType",
                "OccurredAt",
                "Provenance",
                "RecordedAt",
                "Source"
            ],
            PropertyNames(typeof(ClinicalHistoryEventDetailResponse)));
        Assert.Equal(
            ["AmendmentId", "Author", "CreatedAt", "Provenance", "Reason"],
            PropertyNames(typeof(ClinicalHistoryAmendmentResponse)));
        Assert.Equal(
            ["AdditionalFields", "IdempotencyKey", "Reason"],
            PropertyNames(typeof(AmendPreTriageEpisodeRequest)));
        Assert.NotNull(typeof(AmendPreTriageEpisodeRequest)
            .GetProperty("AdditionalFields")!
            .GetCustomAttributes(inherit: false)
            .Single(attribute => attribute.GetType().Name == "JsonExtensionDataAttribute"));
    }

    [Fact]
    public void HistoryLayers_ContainNoDestructiveOrDeferredPhase6Capability()
    {
        var assemblies = new[]
        {
            typeof(ClinicalHistoryEvent).Assembly,
            typeof(ListClinicalHistory).Assembly,
            typeof(ClinicalHistoryEndpointExtensions).Assembly
        };
        var historyTypes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace?.EndsWith(".History", StringComparison.Ordinal) == true)
            .ToArray();
        var forbiddenTypeTerms = new[]
        {
            "Fhir", "Hl7", "ConversationHistory", "Diagnosis", "Prescription",
            "TreatmentRecommendation"
        };
        var forbiddenMutationTerms = new[]
        {
            "Delete", "Remove", "Overwrite", "Replace", "UpdateAmendment"
        };

        Assert.NotEmpty(historyTypes);
        Assert.All(historyTypes, type => Assert.DoesNotContain(
            forbiddenTypeTerms,
            term => type.FullName!.Contains(term, StringComparison.OrdinalIgnoreCase)));
        Assert.All(
            historyTypes.SelectMany(type => type.GetMethods()
                .Where(method => method.DeclaringType == type)),
            method => Assert.DoesNotContain(
                forbiddenMutationTerms,
                term => method.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static string[] PropertyNames(Type type) => type
        .GetProperties()
        .Select(property => property.Name)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
}
