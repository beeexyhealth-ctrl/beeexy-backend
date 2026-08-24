using System.Text.Json;
using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Interoperability;

public sealed class QuestionnaireResponseMapperTests
{
    [Fact]
    public void Map_CompletedEpisodeProducesTruthfulReleaseNeutralRepresentation()
    {
        var graph = CreateDemoGraph();
        var specification = FhirMappingSpecificationIdentity.Create("phase-6.3-test");
        var input = QuestionnaireResponseMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Questionnaire);

        var representation = new QuestionnaireResponseMapper(specification).Map(input);

        Assert.IsAssignableFrom<
            IFhirMapper<QuestionnaireResponseMappingInput, QuestionnaireResponseRepresentation>>(
                new QuestionnaireResponseMapper(specification));
        Assert.Equal(FhirConceptualResource.QuestionnaireResponse, representation.Resource);
        Assert.Equal("completed", representation.Status);
        Assert.Equal(graph.PatientId, representation.SourcePatientProfileId);
        Assert.Equal(graph.HistoryEvent.Id, representation.SourceClinicalHistoryEventId);
        Assert.Equal(graph.Episode.Id, representation.SourceEpisodeId);
        Assert.Equal(graph.Questionnaire.Id, representation.SourceQuestionnaireVersionId);
        Assert.Equal(
            graph.Questionnaire.QuestionnaireCode.Value,
            representation.SourceQuestionnaireCode);
        Assert.Equal(
            graph.Questionnaire.Version.Value,
            representation.SourceQuestionnaireVersion);
        Assert.Equal(
            graph.Questionnaire.ContentHash.Value,
            representation.SourceQuestionnaireContentHash);
        Assert.Equal(graph.Episode.CompletedAt, representation.AuthoredAt);
        Assert.Same(specification, representation.MappingSpecification);
        Assert.Equal(4, representation.Items.Count);
        Assert.All(representation.Items, item =>
        {
            Assert.Null(item.LinkId);
            Assert.Equal(
                QuestionnaireResponseSourceAnswerKind.Object,
                item.Answer.SourceKind);
        });
    }

    [Fact]
    public void Map_OrdersItemsByFrozenQuestionOrderAndIsRepeatableWithoutMutation()
    {
        var graph = CreateGraph(
            version: "historical-v1",
            questions:
            [
                Question("FIRST", "First prompt", 1, "{\"type\":\"number\"}"),
                Question("SECOND", "Second prompt", 2, "{\"type\":\"number\"}")
            ],
            answers:
            [
                new(1, "2", 1),
                new(0, "1", 2)
            ]);
        var input = QuestionnaireResponseMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Questionnaire);
        var mapper = Mapper();
        var beforeAnswers = graph.Episode.Answers
            .Select(answer => (answer.Id, answer.AnswerJson, answer.Sequence))
            .ToArray();

        var first = mapper.Map(input);
        var second = mapper.Map(input);

        Assert.Equal(["FIRST", "SECOND"],
            first.Items.Select(item => item.SourceQuestionCode));
        Assert.Equal(first.Items, second.Items);
        Assert.Equal(first.UnresolvedRequirements, second.UnresolvedRequirements);
        Assert.Equal(beforeAnswers, graph.Episode.Answers
            .Select(answer => (answer.Id, answer.AnswerJson, answer.Sequence))
            .ToArray());
        Assert.Equal(2, graph.Episode.Answers.Count);
    }

    [Fact]
    public void Map_PreservesNegativeAndFreeTextWhileOmittingUnansweredQuestion()
    {
        const string freeText = "Keep this text exactly: not a diagnosis; café.";
        var graph = CreateGraph(
            version: "typed-v1",
            questions:
            [
                Question("NEGATIVE", "Is this present?", 1, "{\"type\":\"boolean\"}"),
                Question("FREE_TEXT", "Describe it", 2, "{\"type\":\"string\"}"),
                Question("UNANSWERED", "Optional", 3, "{\"type\":\"string\"}")
            ],
            answers:
            [
                new(0, "false", 1),
                new(1, JsonSerializer.Serialize(freeText), 2)
            ]);

        var representation = Mapper().Map(QuestionnaireResponseMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Questionnaire));

        Assert.Equal(2, representation.Items.Count);
        var negative = representation.Items[0].Answer;
        Assert.Equal(QuestionnaireResponseSourceAnswerKind.Boolean, negative.SourceKind);
        Assert.Equal("false", negative.SourceAnswerJson);
        var text = representation.Items[1].Answer;
        Assert.Equal(QuestionnaireResponseSourceAnswerKind.String, text.SourceKind);
        Assert.Equal(JsonSerializer.Serialize(freeText), text.SourceAnswerJson);
        Assert.DoesNotContain(representation.Items,
            item => item.SourceQuestionCode == "UNANSWERED");
    }

    [Fact]
    public void Map_PreservesFrozenAnswerSchemaWithoutTranslatingItsMeaning()
    {
        const string schema =
            "{\"type\":\"duration\",\"units\":[\"DAYS\",\"WEEKS\"]}";
        const string answer = "{\"value\":2,\"unit\":\"DAYS\"}";
        var graph = CreateGraph(
            version: "schema-v1",
            questions: [Question("DURATION", "How long?", 1, schema)],
            answers: [new(0, answer, 1)]);

        var item = Assert.Single(Mapper().Map(
            QuestionnaireResponseMappingInput.Create(
                graph.HistoryEvent,
                graph.Episode,
                graph.Questionnaire)).Items);

        Assert.Equal(schema, item.Answer.SourceAnswerSchemaJson);
        Assert.Equal(answer, item.Answer.SourceAnswerJson);
        Assert.Equal(QuestionnaireResponseSourceAnswerKind.Object, item.Answer.SourceKind);
    }

    [Fact]
    public void Map_LaterQuestionnaireVersionCannotChangeHistoricalRepresentation()
    {
        var historical = CreateGraph(
            version: "historical-v1",
            questions: [Question("TEXT", "Historical prompt", 1, "{\"type\":\"string\"}")],
            answers: [new(0, "\"historical answer\"", 1)]);
        var historicalInput = QuestionnaireResponseMappingInput.Create(
            historical.HistoryEvent,
            historical.Episode,
            historical.Questionnaire);
        var beforePublication = Mapper().Map(historicalInput);
        var laterQuestionnaire = CreateQuestionnaire(
            "historical-v2",
            [Question("TEXT", "Changed prompt", 1, "{\"type\":\"string\"}")]);

        var afterPublication = Mapper().Map(historicalInput);

        Assert.Equal(beforePublication.Items, afterPublication.Items);
        Assert.Equal("historical-v1", afterPublication.SourceQuestionnaireVersion);
        Assert.Equal("Historical prompt", Assert.Single(afterPublication.Items).Text);
        Assert.Throws<FhirMappingInputException>(() =>
            QuestionnaireResponseMappingInput.Create(
                historical.HistoryEvent,
                historical.Episode,
                laterQuestionnaire));
    }

    [Fact]
    public void Map_MissingFrozenAnswerSchemaFailsInsteadOfGuessing()
    {
        var graph = CreateGraph(
            version: "missing-schema-v1",
            questions: [Question("TEXT", "Prompt", 1, null)],
            answers: [new(0, "\"answer\"", 1)]);
        var input = QuestionnaireResponseMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Questionnaire);

        var exception = Assert.Throws<FhirMappingInputException>(() => Mapper().Map(input));

        Assert.Equal("A source answer is missing its frozen answer schema.", exception.Message);
    }

    [Fact]
    public void Map_NullAnswerFailsInsteadOfTreatingItAsMissingOrNegative()
    {
        var graph = CreateGraph(
            version: "null-answer-v1",
            questions: [Question("BOOLEAN", "Prompt", 1, "{\"type\":\"boolean\"}")],
            answers: [new(0, "null", 1)]);

        var exception = Assert.Throws<FhirMappingInputException>(() => Mapper().Map(
            QuestionnaireResponseMappingInput.Create(
                graph.HistoryEvent,
                graph.Episode,
                graph.Questionnaire)));

        Assert.Equal(
            "A submitted source answer has no supported truthful representation.",
            exception.Message);
    }

    [Fact]
    public void Map_LeavesReleaseProfilesReferencesLinkIdsAndTranslationUnresolved()
    {
        var graph = CreateDemoGraph();

        var representation = Mapper().Map(QuestionnaireResponseMappingInput.Create(
            graph.HistoryEvent,
            graph.Episode,
            graph.Questionnaire));

        Assert.Null(representation.MappingSpecification.FhirRelease);
        Assert.Equal(
            FhirProfileResolutionStatus.Unresolved,
            representation.MappingSpecification.ProfileResolution.Status);
        Assert.Null(representation.LogicalId);
        Assert.Null(representation.SubjectReference);
        Assert.Null(representation.QuestionnaireReference);
        Assert.All(representation.Items, item => Assert.Null(item.LinkId));
        Assert.False(representation.CanSerializeAsFhir);
        Assert.Equal(
            [
                FhirUnresolvedMappingRequirement.FhirRelease,
                FhirUnresolvedMappingRequirement.CanonicalProfilesAndVersions,
                FhirUnresolvedMappingRequirement.QuestionnaireResponseResourceIdentity,
                FhirUnresolvedMappingRequirement.PatientResourceIdentity,
                FhirUnresolvedMappingRequirement.QuestionnaireResourceIdentityAndVersionEncoding,
                FhirUnresolvedMappingRequirement.QuestionnaireItemLinkIdStrategy,
                FhirUnresolvedMappingRequirement.QuestionnaireAnswerTypeTranslation
            ],
            representation.UnresolvedRequirements);
    }

    [Fact]
    public void Mapper_HasNoAiNormalizationPersistenceHttpOrSerializationDependency()
    {
        var constructor = Assert.Single(typeof(QuestionnaireResponseMapper).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal(typeof(FhirMappingSpecificationIdentity), parameter.ParameterType);
        Assert.DoesNotContain(typeof(QuestionnaireResponseMapper).GetMethods(), method =>
            method.Name.Contains("Serialize", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Persist", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Normalize", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Validate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(QuestionnaireResponseMapper).GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic), field =>
            field.FieldType.Name.Contains("Ai", StringComparison.OrdinalIgnoreCase) ||
            field.FieldType.Name.Contains("Http", StringComparison.OrdinalIgnoreCase) ||
            field.FieldType.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Representation_CarriesNoAuthorizationOrAuthenticationMetadata()
    {
        var propertyNames = typeof(QuestionnaireResponseRepresentation)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Authentication", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Manager", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Authoriz", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QuestionnaireResponseMapper_GeneratesNoOtherResourceOrClinicalConclusion()
    {
        var forbiddenProperties = new[]
        {
            "Urgency", "Disposition", "Diagnosis", "Probability", "Treatment",
            "Prescription", "Prediction", "Mitigation", "RedFlag"
        };

        Assert.DoesNotContain(
            typeof(QuestionnaireResponseRepresentation).GetProperties(),
            property => forbiddenProperties.Any(value =>
                property.Name.Contains(value, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(
            typeof(QuestionnaireResponseMapper).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType.Name is
                "RiskAssessmentMapper" or "DeviceMapper" or "ProvenanceMapper");
    }

    private static QuestionnaireResponseMapper Mapper() => new(
        FhirMappingSpecificationIdentity.Create("phase-6.3-test"));

    private static SourceGraph CreateDemoGraph()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var answers = package.Questionnaire.Questions
            .OrderBy(question => question.DisplayOrder)
            .Select((question, index) => new AnswerDefinition(
                index,
                question.Code.Value switch
                {
                    SimplifiedDemoDefinitionPackages.PrimarySymptomQuestion =>
                        "{\"value\":\"HEADACHE\"}",
                    SimplifiedDemoDefinitionPackages.DurationQuestion =>
                        "{\"value\":2,\"unit\":\"DAYS\"}",
                    SimplifiedDemoDefinitionPackages.IntensityQuestion =>
                        "{\"value\":5}",
                    _ => "{\"values\":[]}"
                },
                index + 1))
            .ToArray();
        return CreateGraph(package.Questionnaire, answers);
    }

    private static SourceGraph CreateGraph(
        string version,
        IReadOnlyList<TriageQuestionInput> questions,
        IReadOnlyList<AnswerDefinition> answers) =>
        CreateGraph(CreateQuestionnaire(version, questions), answers);

    private static SourceGraph CreateGraph(
        QuestionnaireDefinitionVersion questionnaire,
        IReadOnlyList<AnswerDefinition> answers)
    {
        var patientId = EntityId.New();
        var session = PreTriageSession.CreateForPatient(
            patientId,
            questionnaire.Id,
            Utc(20),
            Utc(12));
        var questionValues = questionnaire.Questions
            .OrderBy(question => question.DisplayOrder)
            .ToArray();
        foreach (var answer in answers)
        {
            session.RecordAnswer(
                questionValues[answer.QuestionIndex],
                answer.Json,
                answer.Sequence,
                Utc(13));
        }

        var episode = PreTriageEpisode.CreateFrom(
            session,
            EntityId.New(),
            Utc(14));
        var historyEvent = ClinicalHistoryEvent.CreateCompletedPreTriage(
            episode,
            Utc(15));
        return new SourceGraph(patientId, questionnaire, episode, historyEvent);
    }

    private static QuestionnaireDefinitionVersion CreateQuestionnaire(
        string version,
        IReadOnlyList<TriageQuestionInput> questions) =>
        QuestionnaireDefinitionVersion.ImportApproved(
            QuestionnaireCode.Create("phase-6.3-questionnaire"),
            DefinitionVersion.Create(version),
            DefinitionHash.FromHash(new string('a', 64)),
            Utc(10),
            Utc(11),
            id: EntityId.New(),
            questions: questions);

    private static TriageQuestionInput Question(
        string code,
        string prompt,
        int order,
        string? answerSchemaJson) =>
        new(
            QuestionCode.Create(code),
            prompt,
            order,
            answerSchemaJson,
            Id: EntityId.New());

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 8, 24, hour, 0, 0, TimeSpan.Zero);

    private sealed record AnswerDefinition(
        int QuestionIndex,
        string Json,
        int Sequence);

    private sealed record SourceGraph(
        EntityId PatientId,
        QuestionnaireDefinitionVersion Questionnaire,
        PreTriageEpisode Episode,
        ClinicalHistoryEvent HistoryEvent);
}
