using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Interoperability;

public sealed class ProvenanceMapperTests
{
    [Fact]
    public void Map_PreservesAuthoritativeSourcesAndInternalGenerationTrace()
    {
        var graph = CreateGraph();
        var trace = CreateTrace(Utc(18));
        var specification = Specification();
        var input = ProvenanceMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Assessment,
            trace);

        var representation = new ProvenanceMapper(specification).Map(input);

        Assert.Equal(FhirConceptualResource.Provenance, representation.Resource);
        Assert.Equal(trace.ExportId, representation.ExportId);
        Assert.Same(trace.Provenance, representation.InternalProvenanceIdentity);
        Assert.Same(trace.RiskAssessment, representation.InternalTargetIdentity);
        Assert.Same(trace.Device, representation.InternalAgentIdentity);
        Assert.Same(
            trace.QuestionnaireResponse,
            representation.InternalSourceEntityIdentity);
        Assert.Equal(graph.PatientId, representation.SourcePatientProfileId);
        Assert.Equal(graph.HistoryEvent.Id, representation.SourceClinicalHistoryEventId);
        Assert.Equal(graph.Episode.Id, representation.SourceEpisodeId);
        Assert.Equal(graph.Assessment.Id, representation.SourceAssessmentId);
        Assert.Equal(trace.RecordedAt, representation.RecordedAt);
        Assert.Same(specification, representation.MappingSpecification);
    }

    [Fact]
    public void Map_PreservesAndreaActivityAgentAndSourceConcepts()
    {
        var representation = Map(CreateGraph(), CreateTrace(Utc(18)));

        Assert.Equal(
            "http://terminology.hl7.org/CodeSystem/v3-DataOperation",
            representation.Activity.System);
        Assert.Equal("CREATE", representation.Activity.Code);
        Assert.Equal("create", representation.Activity.Display);
        Assert.Equal(
            "http://terminology.hl7.org/CodeSystem/provenance-participant-type",
            representation.AgentType.System);
        Assert.Equal("author", representation.AgentType.Code);
        Assert.Equal("Author", representation.AgentType.Display);
        Assert.Equal("source", representation.EntityRole);
    }

    [Fact]
    public void Map_UsesGenerationRecordedTimeAndPreservesMappingVersion()
    {
        var graph = CreateGraph();
        var recordedAt = Utc(19);
        var representation = Map(graph, CreateTrace(recordedAt));

        Assert.Equal(recordedAt, representation.RecordedAt);
        Assert.Equal(
            "phase-6.4-test",
            representation.MappingSpecification.MappingVersion);
        Assert.NotEqual(graph.HistoryEvent.RecordedAt, representation.RecordedAt);
    }

    [Fact]
    public void Map_IsDeterministicAndDoesNotMutateSourceRecords()
    {
        var graph = CreateGraph();
        var trace = CreateTrace(Utc(18));
        var input = ProvenanceMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Assessment,
            trace);
        var mapper = new ProvenanceMapper(Specification());
        var before = SourceSnapshot.From(graph);

        var first = mapper.Map(input);
        var second = mapper.Map(input);

        Assert.Equal(first.Activity, second.Activity);
        Assert.Equal(first.AgentType, second.AgentType);
        Assert.Equal(first.RecordedAt, second.RecordedAt);
        Assert.Equal(first.UnresolvedRequirements, second.UnresolvedRequirements);
        Assert.Equal(before, SourceSnapshot.From(graph));
    }

    [Fact]
    public void Representation_DoesNotFabricateFinalFhirReferences()
    {
        var representation = Map(CreateGraph(), CreateTrace(Utc(18)));

        Assert.Null(representation.TargetReference);
        Assert.Null(representation.AgentReference);
        Assert.Null(representation.SourceEntityReference);
        Assert.Equal(
            [
                FhirUnresolvedMappingRequirement.FhirRelease,
                FhirUnresolvedMappingRequirement.CanonicalProfilesAndVersions,
                FhirUnresolvedMappingRequirement.ResourceIdentityAndReferenceStrategy
            ],
            representation.UnresolvedRequirements);
        Assert.False(representation.CanSerializeAsFhir);
        Assert.DoesNotContain(
            new[]
            {
                representation.InternalTargetIdentity.LogicalId,
                representation.InternalAgentIdentity.LogicalId,
                representation.InternalSourceEntityIdentity.LogicalId
            },
            value => value.Contains('/', StringComparison.Ordinal));
    }

    [Fact]
    public void Representation_ExposesNoAuthenticationAuthorizationOrSecretMetadata()
    {
        var propertyNames = typeof(ProvenanceRepresentation).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Authentication", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Authoriz", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Manager", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Capability", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Storage", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(ProvenanceMapper).GetMethods(),
            method => method.Name.Contains("Authoriz", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Access", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InternalGenerationIdentitiesConferNoPatientAuthorization()
    {
        var representation = Map(CreateGraph(), CreateTrace(Utc(18)));

        Assert.DoesNotContain(
            typeof(FhirLogicalResourceIdentity).GetProperties(),
            property => property.Name.Contains("Patient", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Authoriz", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(ProvenanceRepresentation).GetMethods(),
            method => method.Name.Contains("Authoriz", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Access", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            FhirConceptualResource.RiskAssessment,
            representation.InternalTargetIdentity.Resource);
    }

    private static ProvenanceRepresentation Map(
        SourceGraph graph,
        FhirGenerationTrace trace) =>
        new ProvenanceMapper(Specification()).Map(ProvenanceMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Assessment,
            trace));

    private static FhirMappingSpecificationIdentity Specification() =>
        FhirMappingSpecificationIdentity.Create("phase-6.4-test");

    private static FhirGenerationTrace CreateTrace(DateTimeOffset recordedAt) =>
        FhirGenerationTrace.Create(
            EntityId.New(),
            FhirLogicalResourceIdentity.Create(
                FhirConceptualResource.QuestionnaireResponse,
                "internal-questionnaire-response"),
            FhirLogicalResourceIdentity.Create(
                FhirConceptualResource.RiskAssessment,
                "internal-risk-assessment"),
            FhirLogicalResourceIdentity.Create(
                FhirConceptualResource.Device,
                "internal-device"),
            FhirLogicalResourceIdentity.Create(
                FhirConceptualResource.Provenance,
                "internal-provenance"),
            recordedAt);

    private static SourceGraph CreateGraph()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var patientId = EntityId.New();
        var session = PreTriageSession.CreateForPatient(
            patientId,
            package.Questionnaire.Id,
            Utc(20),
            Utc(12));
        var episode = PreTriageEpisode.CreateFrom(
            session,
            package.RuleSet.Id,
            Utc(14));
        var assessment = ClinicalAssessment.CreateNeutral(episode, Utc(15));
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            episode,
            Utc(16));
        return new SourceGraph(patientId, episode, assessment, historyEvent);
    }

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 8, 24, hour, 0, 0, TimeSpan.Zero);

    private sealed record SourceGraph(
        EntityId PatientId,
        PreTriageEpisode Episode,
        ClinicalAssessment Assessment,
        ClinicalHistoryEvent HistoryEvent);

    private sealed record SourceSnapshot(
        EntityId EpisodeId,
        EntityId? PatientId,
        DateTimeOffset CompletedAt,
        EntityId AssessmentId,
        DateTimeOffset AssessmentCreatedAt,
        EntityId HistoryEventId,
        DateTimeOffset HistoryRecordedAt)
    {
        public static SourceSnapshot From(SourceGraph graph) => new(
            graph.Episode.Id,
            graph.Episode.PatientProfileId,
            graph.Episode.CompletedAt,
            graph.Assessment.Id,
            graph.Assessment.CreatedAt,
            graph.HistoryEvent.Id,
            graph.HistoryEvent.RecordedAt);
    }
}
