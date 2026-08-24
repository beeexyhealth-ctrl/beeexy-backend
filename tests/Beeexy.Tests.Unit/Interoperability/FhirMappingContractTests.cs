using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Interoperability;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Interoperability;

public sealed class FhirMappingContractTests
{
    [Fact]
    public void AndreaInventory_DefinesExactlyTheFourRequiredMappingContracts()
    {
        Assert.Equal(
            [
                FhirConceptualResource.QuestionnaireResponse,
                FhirConceptualResource.RiskAssessment,
                FhirConceptualResource.Device,
                FhirConceptualResource.Provenance
            ],
            AndreaFhirMappingInventory.Contracts.Select(contract => contract.Resource));
        Assert.Equal(
            [
                typeof(QuestionnaireResponseMappingInput),
                typeof(RiskAssessmentMappingInput),
                typeof(DeviceMappingInput),
                typeof(ProvenanceMappingInput)
            ],
            AndreaFhirMappingInventory.Contracts.Select(contract => contract.InputType));
        Assert.Equal(4, AndreaFhirMappingInventory.Contracts.Count);
        Assert.True(typeof(IFhirMapper<,>).IsInterface);
        Assert.Equal(
            [
                "docs/fhir/beeexy-coleccion-recursos.md",
                "docs/fhir/beeexy-provenance-device-ejemplo.md",
                "docs/fhir/beeexy-riskassessment-ejemplo.md"
            ],
            AndreaFhirMappingInventory.SourceDocuments);
    }

    [Fact]
    public void QuestionnaireResponseInput_PreservesFrozenEpisodeQuestionnaireAndAnswers()
    {
        var graph = CreateNeutralGraph();

        var input = QuestionnaireResponseMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Package.Questionnaire);

        Assert.Equal(graph.PatientId, input.PatientProfileId);
        Assert.Equal(graph.HistoryEvent.Id, input.SourceClinicalHistoryEventId);
        Assert.Equal(graph.Episode.Id, input.EpisodeId);
        Assert.Equal(graph.Package.Questionnaire.Id, input.QuestionnaireVersionId);
        Assert.Equal(
            graph.Package.Questionnaire.QuestionnaireCode.Value,
            input.QuestionnaireCode);
        Assert.Equal(
            graph.Package.Questionnaire.Version.Value,
            input.QuestionnaireVersion);
        Assert.Equal(
            graph.Package.Questionnaire.ContentHash.Value,
            input.QuestionnaireContentHash);
        Assert.Equal(graph.Episode.CompletedAt, input.AuthoredAt);
        Assert.Equal(4, input.Answers.Count);
        Assert.Equal(
            graph.Package.Questionnaire.Questions
                .OrderBy(question => question.DisplayOrder)
                .Select(question => question.Code.Value),
            input.Answers.Select(answer => answer.QuestionCode));
        Assert.All(input.Answers, answer =>
        {
            Assert.False(string.IsNullOrWhiteSpace(answer.AnswerJson));
            Assert.False(string.IsNullOrWhiteSpace(answer.AnswerSchemaJson));
            Assert.False(string.IsNullOrWhiteSpace(answer.PromptText));
        });
        var symptom = Assert.Single(input.Symptoms);
        Assert.Equal("http://snomed.info/sct", symptom.TerminologySystem);
        Assert.Equal("25064002", symptom.TerminologyCode);
        Assert.Equal("Headache", symptom.TerminologyDisplay);
    }

    [Fact]
    public void QuestionnaireResponseInput_MissingOrMismatchedSourcesFailExplicitly()
    {
        var graph = CreateNeutralGraph();
        var otherPackage = SimplifiedDemoDefinitionPackages.Create(
            ClinicalPathways.AbdominalPain);
        var emptySession = PreTriageSession.CreateForPatient(
            EntityId.New(),
            graph.Package.Questionnaire.Id,
            Utc(20),
            Utc(12));
        var emptyEpisode = PreTriageEpisode.CreateFrom(
            emptySession,
            graph.Package.RuleSet.Id,
            Utc(14));
        var emptyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            emptyEpisode,
            Utc(15));

        Assert.Throws<FhirMappingInputException>(() =>
            QuestionnaireResponseMappingInput.Create(
                graph.HistoryEvent,
                graph.Episode,
                otherPackage.Questionnaire));
        Assert.Throws<FhirMappingInputException>(() =>
            QuestionnaireResponseMappingInput.Create(
                emptyEvent,
                emptyEpisode,
                graph.Package.Questionnaire));
    }

    [Fact]
    public void RiskAssessmentInput_UsesOnlyNeutralAssessmentAndExposesMissingClinicalInputs()
    {
        var graph = CreateNeutralGraph();

        var input = RiskAssessmentMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Assessment);

        Assert.Equal(graph.PatientId, input.PatientProfileId);
        Assert.Equal(graph.HistoryEvent.Id, input.SourceClinicalHistoryEventId);
        Assert.Equal(graph.Episode.Id, input.EpisodeId);
        Assert.Equal(graph.Assessment.Id, input.AssessmentId);
        Assert.Equal(
            graph.Episode.ClinicalRuleSetVersionId,
            input.ClinicalRuleSetVersionId);
        Assert.Equal(graph.Assessment.CreatedAt, input.OccurrenceAt);
        Assert.False(input.IsResourceGenerationReady);
        Assert.Equal(
            [
                FhirUnresolvedMappingRequirement.RiskPredictionOutcome,
                FhirUnresolvedMappingRequirement.RiskPredictionProbability,
                FhirUnresolvedMappingRequirement.RiskMitigation
            ],
            input.UnresolvedRequirements);

        var forbiddenClinicalProperties = new[]
        {
            "Urgency",
            "Disposition",
            "Diagnosis",
            "Probability",
            "Treatment",
            "Prescription",
            "Prediction",
            "Mitigation",
            "RedFlag"
        };
        Assert.DoesNotContain(
            typeof(RiskAssessmentMappingInput).GetProperties(),
            property => forbiddenClinicalProperties.Any(value =>
                property.Name.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void RiskAssessmentInput_RejectsClinicalAuthorityNotPresentInCurrentNeutralSource()
    {
        var graph = CreateNeutralGraph();
        var nonNeutralAssessment = ClinicalAssessment.Create(
            graph.Episode,
            UrgencyCode.Create("unapproved-urgency"),
            Utc(14));

        Assert.Throws<FhirMappingInputException>(() =>
            RiskAssessmentMappingInput.Create(
                graph.HistoryEvent,
                graph.Episode,
                nonNeutralAssessment));
    }

    [Fact]
    public void DeviceInput_UsesOnlyAndreaSupportedIdentityAndExplicitRuntimeVersion()
    {
        var input = DeviceMappingInput.Create("runtime-version-from-generator");

        Assert.Equal("Beeexy Triage Engine", input.DeviceName);
        Assert.Equal("manufacturer-name", input.DeviceNameType);
        Assert.Equal("triage-core", input.ModelNumber);
        Assert.Equal("runtime-version-from-generator", input.SoftwareVersion);
        Assert.Equal("Beeexy Inc.", input.Manufacturer);
        Assert.Equal("Clinical decision support software", input.TypeText);
        Assert.Throws<ArgumentException>(() => DeviceMappingInput.Create("  "));
    }

    [Fact]
    public void ProvenanceInput_PreservesTargetAgentSourceAndInternalTraceability()
    {
        var graph = CreateNeutralGraph();
        var trace = CreateGenerationTrace(Utc(17));

        var input = ProvenanceMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Assessment,
            trace);

        Assert.Equal(graph.PatientId, input.PatientProfileId);
        Assert.Equal(graph.HistoryEvent.Id, input.SourceClinicalHistoryEventId);
        Assert.Equal(graph.Episode.Id, input.SourceEpisodeId);
        Assert.Equal(graph.Assessment.Id, input.SourceAssessmentId);
        Assert.Same(trace, input.GenerationTrace);
        Assert.Equal(FhirConceptualResource.RiskAssessment, input.Target.Resource);
        Assert.Equal(FhirConceptualResource.Device, input.Agent.Resource);
        Assert.Equal(
            FhirConceptualResource.QuestionnaireResponse,
            input.SourceEntity.Resource);
        Assert.Throws<FhirMappingInputException>(() => ProvenanceMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Assessment,
            CreateGenerationTrace(Utc(13))));
    }

    [Fact]
    public void LogicalResourceIdentifiers_CannotActAsPatientAuthorization()
    {
        var identity = FhirLogicalResourceIdentity.Create(
            FhirConceptualResource.RiskAssessment,
            "outbound-reference-only");

        Assert.Equal("outbound-reference-only", identity.LogicalId);
        Assert.DoesNotContain(
            typeof(FhirLogicalResourceIdentity).GetProperties(),
            property => property.Name.Contains("Patient", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Authoriz", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(FhirLogicalResourceIdentity).GetMethods(),
            method => method.Name.Contains("Authoriz", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Access", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MappingSpecification_RequiresExplicitReleaseAndProfileResolution()
    {
        var unresolved = FhirMappingSpecificationIdentity.Create(
            "mapping-version-from-approved-specification");

        Assert.Null(unresolved.FhirRelease);
        Assert.Equal(
            FhirProfileResolutionStatus.Unresolved,
            unresolved.ProfileResolution.Status);
        Assert.False(unresolved.IsReadyForExport);
        Assert.Throws<InvalidOperationException>(() =>
            unresolved.ToExportVersionMetadata());

        var noProfiles = FhirMappingSpecificationIdentity.Create(
            "mapping-version-from-approved-specification",
            "release-from-approved-configuration",
            FhirProfileResolution.NotApplicable());
        var exportVersions = noProfiles.ToExportVersionMetadata();
        Assert.Equal("release-from-approved-configuration", exportVersions.FhirVersion);
        Assert.Equal(
            "mapping-version-from-approved-specification",
            exportVersions.MappingVersion);
        Assert.Null(exportVersions.ProfileCanonical);
        Assert.Null(exportVersions.ProfileVersion);

        var profiled = FhirMappingSpecificationIdentity.Create(
            "mapping-version-from-approved-specification",
            "release-from-approved-configuration",
            FhirProfileResolution.Specified(
                "canonical-from-approved-configuration",
                "profile-version-from-approved-configuration"));
        Assert.True(profiled.IsReadyForExport);
        Assert.Equal(
            "canonical-from-approved-configuration",
            profiled.ToExportVersionMetadata().ProfileCanonical);
    }

    [Fact]
    public void Domain_RemainsIndependentOfMappingContractsAndFhirSdks()
    {
        var domainAssembly = typeof(FhirExport).Assembly;

        Assert.DoesNotContain(domainAssembly.GetReferencedAssemblies(), assembly =>
            assembly.Name!.Contains("FHIR", StringComparison.OrdinalIgnoreCase) ||
            assembly.Name.Contains("HL7", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(domainAssembly.GetTypes(), type =>
            type.Namespace?.StartsWith(
                "Beeexy.Application.Interoperability",
                StringComparison.Ordinal) == true);
        Assert.Empty(typeof(FhirExport).Assembly
            .GetReferencedAssemblies()
            .Where(assembly => assembly.Name == "Beeexy.Application"));
    }

    private static NeutralGraph CreateNeutralGraph()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var patientId = EntityId.New();
        var session = PreTriageSession.CreateForPatient(
            patientId,
            package.Questionnaire.Id,
            Utc(20),
            Utc(12));
        var answerValues = new Dictionary<string, string>
        {
            [SimplifiedDemoDefinitionPackages.PrimarySymptomQuestion] =
                "{\"value\":\"HEADACHE\"}",
            [SimplifiedDemoDefinitionPackages.DurationQuestion] =
                "{\"value\":2,\"unit\":\"DAYS\"}",
            [SimplifiedDemoDefinitionPackages.IntensityQuestion] =
                "{\"value\":5}",
            [SimplifiedDemoDefinitionPackages.AdditionalSymptomsQuestion] =
                "{\"values\":[]}"
        };
        foreach (var question in package.Questionnaire.Questions
                     .OrderBy(question => question.DisplayOrder))
        {
            session.RecordAnswer(
                question,
                answerValues[question.Code.Value],
                question.DisplayOrder,
                Utc(13));
        }

        session.ReportSymptom(
            SymptomText.Create("Headache"),
            sequence: 1,
            reportedAt: Utc(13),
            terminologySystem: "http://snomed.info/sct",
            terminologyCode: "25064002",
            terminologyDisplay: "Headache",
            normalizationSource: "test-source",
            normalizedAt: Utc(13));
        var episode = PreTriageEpisode.CreateFrom(
            session,
            package.RuleSet.Id,
            Utc(14));
        var assessment = ClinicalAssessment.CreateNeutral(episode, Utc(14));
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            episode,
            Utc(15));
        return new NeutralGraph(
            patientId,
            package,
            episode,
            assessment,
            historyEvent);
    }

    private static FhirGenerationTrace CreateGenerationTrace(DateTimeOffset recordedAt)
    {
        return FhirGenerationTrace.Create(
            EntityId.New(),
            FhirLogicalResourceIdentity.Create(
                FhirConceptualResource.QuestionnaireResponse,
                "questionnaire-response-id"),
            FhirLogicalResourceIdentity.Create(
                FhirConceptualResource.RiskAssessment,
                "risk-assessment-id"),
            FhirLogicalResourceIdentity.Create(
                FhirConceptualResource.Device,
                "device-id"),
            FhirLogicalResourceIdentity.Create(
                FhirConceptualResource.Provenance,
                "provenance-id"),
            recordedAt);
    }

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 8, 24, hour, 0, 0, TimeSpan.Zero);

    private sealed record NeutralGraph(
        EntityId PatientId,
        ClinicalDefinitionPackage Package,
        PreTriageEpisode Episode,
        ClinicalAssessment Assessment,
        ClinicalHistoryEvent HistoryEvent);
}
