using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Triage;

namespace Beeexy.Tests.Unit.Interoperability;

internal static class FhirSnapshotTestData
{
    public static TestGraph CreateGraph(string version = "historical-v1")
    {
        var questionnaire = QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create("phase-6.5-questionnaire"),
            DefinitionVersion.Create(version),
            DefinitionHash.FromHash(new string('a', 64)),
            Utc(10),
            Utc(11),
            id: EntityId.New(),
            questions:
            [
                new TriageQuestionInput(
                    QuestionCode.Create("SYMPTOM_TEXT"),
                    "Describe the symptom",
                    1,
                    "{\"type\":\"string\"}",
                    Id: EntityId.New())
            ]);
        var patientId = EntityId.New();
        var session = PreTriageSession.CreateForPatient(
            patientId,
            questionnaire.Id,
            Utc(20),
            Utc(12));
        session.RecordAnswer(
            Assert.Single(questionnaire.Questions),
            "\"historical answer\"",
            1,
            Utc(13));
        var episode = PreTriageEpisode.CreateFrom(
            session,
            EntityId.New(),
            Utc(14));
        var assessment = ClinicalAssessment.CreateNeutral(episode, Utc(15));
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            episode,
            Utc(16));
        return new TestGraph(
            patientId,
            questionnaire,
            episode,
            assessment,
            historyEvent);
    }

    public static FhirSnapshot CreateSnapshot(TestGraph? graph = null)
    {
        graph ??= CreateGraph();
        var exportId = EntityId.From(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var trace = FhirGenerationTrace.Create(
            exportId,
            Identity(FhirConceptualResource.QuestionnaireResponse, exportId),
            Identity(FhirConceptualResource.RiskAssessment, exportId),
            Identity(FhirConceptualResource.Device, exportId),
            Identity(FhirConceptualResource.Provenance, exportId),
            Utc(18));
        var specification = Specification();
        return new FhirSnapshotAssembler(specification).Assemble(
            new FhirSnapshotAssemblyInput(
                QuestionnaireResponseMappingInput.Create(
                    graph.HistoryEvent,
                    graph.Episode,
                    graph.Questionnaire),
                RiskAssessmentMappingInput.Create(
                    graph.HistoryEvent,
                    graph.Episode,
                    graph.Assessment),
                DeviceMappingInput.Create("6.5-test-runtime"),
                ProvenanceMappingInput.Create(
                    graph.HistoryEvent,
                    graph.Episode,
                    graph.Assessment,
                    trace)));
    }

    public static FhirMappingSpecificationIdentity Specification() =>
        FhirMappingSpecificationIdentity.Create("phase-6.5-test");

    public static DateTimeOffset Utc(int hour) =>
        new(2026, 8, 24, hour, 0, 0, TimeSpan.Zero);

    private static FhirLogicalResourceIdentity Identity(
        FhirConceptualResource resource,
        EntityId exportId) => FhirLogicalResourceIdentity.Create(
            resource,
            $"internal-{resource.ToString().ToLowerInvariant()}:{exportId.Value:D}");

    internal sealed record TestGraph(
        EntityId PatientId,
        QuestionnaireDefinitionVersion Questionnaire,
        PreTriageEpisode Episode,
        ClinicalAssessment Assessment,
        ClinicalHistoryEvent HistoryEvent);
}
