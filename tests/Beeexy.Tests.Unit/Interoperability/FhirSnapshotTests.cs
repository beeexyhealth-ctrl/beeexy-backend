using System.Text;
using System.Text.Json;
using Beeexy.Application.Interoperability;

namespace Beeexy.Tests.Unit.Interoperability;

public sealed class FhirSnapshotTests
{
    [Fact]
    public void Assemble_SupportedMappingsUseDeterministicOrderAndExposeMandatoryBlocker()
    {
        var snapshot = FhirSnapshotTestData.CreateSnapshot();

        Assert.Equal(
            [
                FhirConceptualResource.QuestionnaireResponse,
                FhirConceptualResource.Device,
                FhirConceptualResource.Provenance
            ],
            snapshot.ResourceOrder);
        Assert.Equal(
            FhirSnapshotCompleteness.IncompleteRequiredResourceBlocked,
            snapshot.Completeness);
        Assert.False(snapshot.IsOfficialFhirJson);
        Assert.False(snapshot.IsCompleteFhirExport);
        Assert.False(snapshot.CanBeFhirValidated);
        Assert.True(snapshot.RiskAssessmentBoundary.IsConcreteGenerationBlocked);
        Assert.Contains(
            FhirUnresolvedMappingRequirement.RiskPredictionProbability,
            snapshot.UnresolvedRequirements);
    }

    [Fact]
    public void Assemble_IsRepeatableAndDoesNotMutateAuthoritativeRecords()
    {
        var graph = FhirSnapshotTestData.CreateGraph();
        var before = graph.Episode.Answers
            .Select(answer => (answer.Id, answer.AnswerJson, answer.RecordedAt))
            .ToArray();

        var first = FhirSnapshotTestData.CreateSnapshot(graph);
        var second = FhirSnapshotTestData.CreateSnapshot(graph);

        Assert.Equal(first.QuestionnaireResponse.Items, second.QuestionnaireResponse.Items);
        Assert.Equal(first.ResourceOrder, second.ResourceOrder);
        Assert.Equal(before, graph.Episode.Answers
            .Select(answer => (answer.Id, answer.AnswerJson, answer.RecordedAt))
            .ToArray());
    }

    [Fact]
    public void Assemble_PreservesFrozenQuestionnaireAndLaterVersionCannotReplaceIt()
    {
        var historical = FhirSnapshotTestData.CreateGraph("historical-v1");
        var snapshot = FhirSnapshotTestData.CreateSnapshot(historical);
        var later = FhirSnapshotTestData.CreateGraph("later-v2");

        Assert.Equal("historical-v1", snapshot.QuestionnaireResponse.SourceQuestionnaireVersion);
        Assert.Equal("Describe the symptom", Assert.Single(snapshot.QuestionnaireResponse.Items).Text);
        Assert.Throws<FhirMappingInputException>(() =>
            QuestionnaireResponseMappingInput.Create(
                historical.HistoryEvent,
                historical.Episode,
                later.Questionnaire));
    }

    [Fact]
    public void Assembler_HasNoAiClinicalInferenceOrValidatorDependency()
    {
        var dependencyNames = typeof(FhirSnapshotAssembler).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.FieldType.Name)
            .ToArray();

        Assert.DoesNotContain(dependencyNames, name =>
            name.Contains("ClinicalAi", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Validator", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Inference", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Serialize_IsDeterministicReleaseNeutralAndNotOfficialFhirJson()
    {
        var snapshot = FhirSnapshotTestData.CreateSnapshot();
        var serializer = new FhirSnapshotSerializer();

        var first = serializer.Serialize(snapshot);
        var second = serializer.Serialize(snapshot);

        Assert.Equal(first, second);
        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        Assert.Equal(FhirSnapshotArtifactFormat.ArtifactKind,
            root.GetProperty("artifactKind").GetString());
        Assert.Equal(FhirSnapshotArtifactFormat.MediaType,
            root.GetProperty("mediaType").GetString());
        Assert.False(root.GetProperty("officialFhirJson").GetBoolean());
        Assert.False(root.GetProperty("completeFhirExport").GetBoolean());
        Assert.Null(root.GetProperty("mappingSpecification")
            .GetProperty("fhirRelease").GetString());
        Assert.Equal("RiskAssessment", root.GetProperty("blockedRequiredResources")[0]
            .GetProperty("concept").GetString());
        var json = Encoding.UTF8.GetString(first);
        Assert.DoesNotContain("application/fhir+json", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"resourceType\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Bundle\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Checksum_UsesExactBytesAndChangesWithArtifactContent()
    {
        var calculator = new FhirArtifactChecksumCalculator();
        var bytes = new FhirSnapshotSerializer().Serialize(
            FhirSnapshotTestData.CreateSnapshot());
        var sameBytes = bytes.ToArray();
        var changedBytes = bytes.Concat(new byte[] { (byte)' ' }).ToArray();

        var checksum = calculator.Calculate(bytes);

        Assert.Equal(checksum, calculator.Calculate(sameBytes));
        Assert.NotEqual(checksum, calculator.Calculate(changedBytes));
        Assert.Equal(64, checksum.Length);
        Assert.Equal(checksum, checksum.ToLowerInvariant());
    }
}
