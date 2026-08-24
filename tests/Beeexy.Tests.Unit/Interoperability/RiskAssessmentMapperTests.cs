using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Interoperability;

public sealed class RiskAssessmentMapperTests
{
    [Fact]
    public void Inspect_PreservesOnlyTruthfulNeutralAssessmentFacts()
    {
        var graph = CreateGraph();
        var specification = Specification();
        var input = RiskAssessmentMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Assessment);

        var boundary = new RiskAssessmentMapper(specification).Inspect(input);

        Assert.Equal(FhirConceptualResource.RiskAssessment, boundary.Resource);
        Assert.Equal("final", boundary.SupportedStatusConcept);
        Assert.Equal(
            AndreaFhirMappingInventory.RiskAssessmentDisclaimer,
            boundary.SupportedDisclaimerConcept);
        Assert.Equal(graph.PatientId, boundary.SourcePatientProfileId);
        Assert.Equal(graph.HistoryEvent.Id, boundary.SourceClinicalHistoryEventId);
        Assert.Equal(graph.Episode.Id, boundary.SourceEpisodeId);
        Assert.Equal(graph.Assessment.Id, boundary.SourceAssessmentId);
        Assert.Equal(
            graph.Assessment.ClinicalRuleSetVersionId,
            boundary.SourceClinicalRuleSetVersionId);
        Assert.Equal(graph.Assessment.CreatedAt, boundary.OccurrenceAt);
        Assert.Same(specification, boundary.MappingSpecification);
        Assert.True(boundary.IsConcreteGenerationBlocked);
        Assert.False(boundary.CanSerializeAsFhir);
    }

    [Fact]
    public void Map_NeutralAssessmentFailsWithExactAuthoritativeInputBlockers()
    {
        var graph = CreateGraph();
        var mapper = Mapper();
        var input = RiskAssessmentMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Assessment);

        var exception = Assert.Throws<RiskAssessmentGenerationBlockedException>(
            () => mapper.Map(input));

        Assert.Equal(
            "RiskAssessment generation is blocked because the authoritative " +
            "ClinicalAssessment has no prediction outcome, probability, or mitigation.",
            exception.Message);
        Assert.Equal(
            [
                FhirUnresolvedMappingRequirement.FhirRelease,
                FhirUnresolvedMappingRequirement.CanonicalProfilesAndVersions,
                FhirUnresolvedMappingRequirement.PatientResourceIdentity,
                FhirUnresolvedMappingRequirement.ResourceIdentityAndReferenceStrategy,
                FhirUnresolvedMappingRequirement.RiskPredictionOutcome,
                FhirUnresolvedMappingRequirement.RiskPredictionProbability,
                FhirUnresolvedMappingRequirement.RiskMitigation
            ],
            exception.Boundary.UnresolvedRequirements);
        Assert.Null(exception.Boundary.LogicalId);
        Assert.Null(exception.Boundary.SubjectReference);
        Assert.Null(exception.Boundary.BasisReference);
    }

    [Fact]
    public void Boundary_CarriesNoFabricatedPredictionProbabilityOrMitigationValue()
    {
        var graph = CreateGraph();
        var boundary = Mapper().Inspect(RiskAssessmentMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Assessment));
        var values = typeof(RiskAssessmentGenerationBoundary)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.GetValue(boundary) as string)
            .Where(value => value is not null)
            .ToArray();

        Assert.DoesNotContain(values, value =>
            value is "0.25" or "0.50" or "0.72" or "25%" or "50%" or
                "Low risk" or "Moderate risk" or "High risk" or
                "moderate" or "Peripheral vertigo");
        Assert.DoesNotContain(
            typeof(RiskAssessmentGenerationBoundary).GetProperties(),
            property => property.Name is
                "Prediction" or "Probability" or "QualitativeRisk" or "Mitigation");
    }

    [Fact]
    public void QuestionnaireAnswersAndSymptomIntensityCannotBecomeRiskContent()
    {
        var graph = CreateGraph();
        var input = RiskAssessmentMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Assessment);
        var boundary = Mapper().Inspect(input);

        Assert.Contains(graph.Episode.Answers, answer =>
            answer.AnswerJson.Contains("5", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(RiskAssessmentMappingInput).GetProperties(),
            property => property.Name.Contains("Answer", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Symptom", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Intensity", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(RiskAssessmentGenerationBoundary).GetProperties(),
            property => property.Name.Contains("Answer", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Symptom", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Intensity", StringComparison.OrdinalIgnoreCase));
        Assert.True(boundary.IsConcreteGenerationBlocked);
    }

    [Fact]
    public void Inspect_IsRepeatableAndDoesNotMutateSourceAssessment()
    {
        var graph = CreateGraph();
        var input = RiskAssessmentMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Assessment);
        var before = AssessmentSnapshot.From(graph.Assessment);
        var mapper = Mapper();

        var first = mapper.Inspect(input);
        var second = mapper.Inspect(input);

        Assert.Equal(first.SourceAssessmentId, second.SourceAssessmentId);
        Assert.Equal(first.UnresolvedRequirements, second.UnresolvedRequirements);
        Assert.Equal(before, AssessmentSnapshot.From(graph.Assessment));
    }

    [Fact]
    public void Mapper_HasNoAiClinicalRuleOrProviderDependency()
    {
        var constructor = Assert.Single(typeof(RiskAssessmentMapper).GetConstructors());
        Assert.Equal(
            typeof(FhirMappingSpecificationIdentity),
            Assert.Single(constructor.GetParameters()).ParameterType);
        Assert.DoesNotContain(
            typeof(RiskAssessmentMapper).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType.Name.Contains("Ai", StringComparison.OrdinalIgnoreCase) ||
                field.FieldType.Name.Contains("Rule", StringComparison.OrdinalIgnoreCase) ||
                field.FieldType.Name.Contains("Provider", StringComparison.OrdinalIgnoreCase));
    }

    private static RiskAssessmentMapper Mapper() => new(Specification());

    private static FhirMappingSpecificationIdentity Specification() =>
        FhirMappingSpecificationIdentity.Create("phase-6.4-test");

    private static SourceGraph CreateGraph()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var patientId = EntityId.New();
        var session = PreTriageSession.CreateForPatient(
            patientId,
            package.Questionnaire.Id,
            Utc(20),
            Utc(12));
        foreach (var question in package.Questionnaire.Questions
                     .OrderBy(question => question.DisplayOrder))
        {
            var answer = question.Code.Value ==
                SimplifiedDemoDefinitionPackages.IntensityQuestion
                    ? "{\"value\":5}"
                    : "{\"value\":\"source-only\"}";
            session.RecordAnswer(
                question,
                answer,
                question.DisplayOrder,
                Utc(13));
        }

        session.ReportSymptom(
            SymptomText.Create("Headache"),
            sequence: 1,
            reportedAt: Utc(13));
        var episode = PreTriageEpisode.CreateFrom(
            session,
            package.RuleSet.Id,
            Utc(14));
        var assessment = ClinicalAssessment.CreateNeutral(episode, Utc(15));
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            episode,
            Utc(16));
        return new SourceGraph(
            patientId,
            episode,
            assessment,
            historyEvent);
    }

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 8, 24, hour, 0, 0, TimeSpan.Zero);

    private sealed record SourceGraph(
        EntityId PatientId,
        PreTriageEpisode Episode,
        ClinicalAssessment Assessment,
        ClinicalHistoryEvent HistoryEvent);

    private sealed record AssessmentSnapshot(
        EntityId Id,
        EntityId EpisodeId,
        EntityId RuleSetId,
        DateTimeOffset CreatedAt,
        UrgencyCode? Urgency,
        string? Message,
        int FindingCount)
    {
        public static AssessmentSnapshot From(ClinicalAssessment assessment) => new(
            assessment.Id,
            assessment.EpisodeId,
            assessment.ClinicalRuleSetVersionId,
            assessment.CreatedAt,
            assessment.UrgencyCode,
            assessment.ResultMessageReference,
            assessment.Findings.Count);
    }
}
