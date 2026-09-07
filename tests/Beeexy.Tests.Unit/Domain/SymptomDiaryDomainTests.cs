using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Tests.Unit.Domain;

public sealed class SymptomDiaryDomainTests
{
    [Fact]
    [Trait("Category", "Phase91")]
    public void Package_PreservesStableIdentityAndMedicalTeamProvenance()
    {
        var package = CreatePackage();

        Assert.Equal("test-diary", package.PackageCode.Value);
        Assert.Equal("v1", package.PackageVersion.Value);
        Assert.Equal("test-question-set", package.QuestionSetCode.Value);
        Assert.Equal("q1", package.QuestionSetVersion.Value);
        Assert.Equal("test-information", package.SymptomInformationCode.Value);
        Assert.Equal("i1", package.SymptomInformationVersion.Value);
        Assert.Equal(ClinicalContentSource.MedicalTeamProvided, package.ContentSource);
        Assert.Equal(ClinicalReviewStatus.Reviewed, package.ReviewStatus);
        Assert.Equal(ClinicalApprovalStatus.Approved, package.ApprovalStatus);
        Assert.Equal(Utc(10), package.ApprovedAt);
        Assert.Equal(Utc(12), package.ActivatedAt);
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void Sha256_RequiresCanonicalLowercaseShape()
    {
        Assert.Equal(new string('a', 64), SymptomDiarySha256.FromHash(new string('a', 64)).Value);
        Assert.Throws<ArgumentException>(() => SymptomDiarySha256.FromHash(new string('a', 63)));
        Assert.Throws<ArgumentException>(() => SymptomDiarySha256.FromHash(new string('A', 64)));
        Assert.Throws<ArgumentException>(() => SymptomDiarySha256.FromHash(new string('z', 64)));
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void Package_RejectsInvalidIdentityAndApprovalLifecycle()
    {
        Assert.Throws<ArgumentException>(() => SymptomDiaryCode.Create(" "));
        Assert.Throws<ArgumentException>(() => CreatePackage(
            status: ClinicalContentStatus.MedicalTeamApproved,
            omitApprovalTimestamp: true));
        Assert.Throws<ArgumentException>(() => CreatePackage(
            status: ClinicalContentStatus.NonClinicalDemo,
            approvedAt: Utc(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreatePackage(
            activatedAt: Utc(9)));
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void Package_OrdersQuestionsOptionsAndWarningSignsBySourceOrder()
    {
        var package = CreatePackage(
            questions:
            [
                Question("second", 2),
                Question("first", 1,
                    [Option("later", 2), Option("earlier", 1)])
            ],
            warnings:
            [
                new(SymptomDiaryCode.Create("second-warning"), "Synthetic display B", 2),
                new(SymptomDiaryCode.Create("first-warning"), "Synthetic display A", 1)
            ]);

        Assert.Equal([1, 2], package.Questions.Select(value => value.SourceOrder));
        Assert.Equal(
            [1, 2],
            package.Questions.First().Options.Select(value => value.SourceOrder));
        Assert.Equal([1, 2], package.WarningSigns.Select(value => value.SourceOrder));
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void Package_RejectsDuplicateQuestionCodeOrOrder()
    {
        Assert.Throws<InvalidOperationException>(() => CreatePackage(questions:
        [
            Question("duplicate", 1),
            Question("duplicate", 2)
        ]));
        Assert.Throws<InvalidOperationException>(() => CreatePackage(questions:
        [
            Question("first", 1),
            Question("second", 1)
        ]));
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void Question_RejectsDuplicateOptionCodeOrOrderAndInvalidSchema()
    {
        Assert.Throws<InvalidOperationException>(() => CreatePackage(questions:
        [Question("question", 1, [Option("duplicate", 1), Option("duplicate", 2)])]));
        Assert.Throws<InvalidOperationException>(() => CreatePackage(questions:
        [Question("question", 1, [Option("first", 1), Option("second", 1)])]));
        Assert.Throws<ArgumentException>(() => CreatePackage(questions:
        [new(SymptomDiaryCode.Create("question"), "Synthetic prompt", 1, "not-json", true)]));
        Assert.Throws<ArgumentException>(() => CreatePackage(questions:
        [new(SymptomDiaryCode.Create("question"), "Synthetic prompt", 1, "true", true)]));
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void StaticContent_PreservesExactSourceTextWithoutTrimming()
    {
        var package = CreatePackage(
            questions:
            [new(SymptomDiaryCode.Create("question"), "  Exact prompt: ¿sí?  ", 1, "{\"type\":\"string\"}", true,
                [new(SymptomDiaryCode.Create("option"), " exact-value ", "  Exact option — text  ", 1)])],
            warnings:
            [new(SymptomDiaryCode.Create("warning"), "  Exact informational text.  ", 1)]);

        var question = Assert.Single(package.Questions);
        Assert.Equal("  Exact prompt: ¿sí?  ", question.PromptText);
        var option = Assert.Single(question.Options);
        Assert.Equal(" exact-value ", option.Value);
        Assert.Equal("  Exact option — text  ", option.DisplayText);
        Assert.Equal("  Exact informational text.  ", Assert.Single(package.WarningSigns).DisplayText);
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void Package_RejectsDuplicateWarningSignCodeOrOrder()
    {
        Assert.Throws<InvalidOperationException>(() => CreatePackage(warnings:
        [
            new(SymptomDiaryCode.Create("duplicate"), "Synthetic display A", 1),
            new(SymptomDiaryCode.Create("duplicate"), "Synthetic display B", 2)
        ]));
        Assert.Throws<InvalidOperationException>(() => CreatePackage(warnings:
        [
            new(SymptomDiaryCode.Create("first"), "Synthetic display A", 1),
            new(SymptomDiaryCode.Create("second"), "Synthetic display B", 1)
        ]));
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void CheckIn_PreservesNeutralIdentityAndExactJsonAnswers()
    {
        var questionnaire = CreateQuestionnaire();
        var package = CreatePackage();
        var episode = CreatePatientOwnedEpisode(questionnaire);
        var question = Assert.Single(package.Questions);

        var checkIn = SymptomCheckIn.Create(
            episode,
            questionnaire,
            package,
            EntityId.New(),
            EntityId.New(),
            SymptomDiarySha256.FromHash(new string('b', 64)),
            Utc(14),
            [new(question, "{\"nested\":[true,3,\"exact\"]}")]);

        Assert.Equal(episode.Id, checkIn.EpisodeId);
        Assert.Equal(package.Id, checkIn.PackageVersionId);
        Assert.Equal(Utc(14), checkIn.CreatedAt);
        var answer = Assert.Single(checkIn.Answers);
        Assert.Equal(question.Id, answer.QuestionId);
        Assert.Equal(question.SourceOrder, answer.SourceOrder);
        Assert.Equal("{\"nested\":[true,3,\"exact\"]}", answer.SubmittedValueJson);
        Assert.Equal(Utc(14), answer.RecordedAt);
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void CheckIn_RejectsAnonymousEpisodeOrMismatchedFrozenQuestionnaire()
    {
        var questionnaire = CreateQuestionnaire();
        var anonymous = PreTriageSession.CreateAnonymous(
            questionnaire.Id,
            AnonymousCapabilityHash.FromHash(new string('c', 64)),
            Utc(20),
            Utc(11));
        var anonymousEpisode = PreTriageEpisode.CreateFrom(anonymous, EntityId.New(), Utc(13), Utc(19));

        Assert.Throws<ArgumentException>(() => CreateCheckIn(
            anonymousEpisode,
            questionnaire,
            CreatePackage()));

        var patientEpisode = CreatePatientOwnedEpisode(questionnaire);
        Assert.Throws<ArgumentException>(() => CreateCheckIn(
            patientEpisode,
            CreateQuestionnaire("other-questionnaire"),
            CreatePackage()));
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void CheckIn_RejectsCrossPathwayPackageAndQuestion()
    {
        var questionnaire = CreateQuestionnaire();
        var episode = CreatePatientOwnedEpisode(questionnaire);
        var otherPackage = CreatePackage(pathway: ClinicalPathwayCode.Create("OTHER_PATHWAY"));
        Assert.Throws<ArgumentException>(() => CreateCheckIn(episode, questionnaire, otherPackage));

        var package = CreatePackage();
        var foreignQuestion = Assert.Single(CreatePackage(packageCode: "foreign").Questions);
        Assert.Throws<ArgumentException>(() => SymptomCheckIn.Create(
            episode,
            questionnaire,
            package,
            EntityId.New(),
            EntityId.New(),
            SymptomDiarySha256.FromHash(new string('b', 64)),
            Utc(14),
            [new(foreignQuestion, "true")]));
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void CheckIn_RejectsDuplicateQuestionAnswersAndInvalidJson()
    {
        var questionnaire = CreateQuestionnaire();
        var episode = CreatePatientOwnedEpisode(questionnaire);
        var package = CreatePackage();
        var question = Assert.Single(package.Questions);

        Assert.Throws<InvalidOperationException>(() => SymptomCheckIn.Create(
            episode,
            questionnaire,
            package,
            EntityId.New(),
            EntityId.New(),
            SymptomDiarySha256.FromHash(new string('b', 64)),
            Utc(14),
            [new(question, "true"), new(question, "false")]));
        Assert.Throws<ArgumentException>(() => SymptomCheckIn.Create(
            episode,
            questionnaire,
            package,
            EntityId.New(),
            EntityId.New(),
            SymptomDiarySha256.FromHash(new string('b', 64)),
            Utc(14),
            [new(question, "invalid-json")]));
    }

    [Fact]
    [Trait("Category", "Phase91")]
    public void PersistedEntities_AreImmutableAndContainNoClinicalInferenceState()
    {
        var entityTypes = new[]
        {
            typeof(SymptomDiaryPackageVersion),
            typeof(SymptomDiaryQuestion),
            typeof(SymptomDiaryQuestionOption),
            typeof(SymptomWarningSign),
            typeof(SymptomCheckIn),
            typeof(SymptomCheckInAnswer)
        };
        var forbidden = new[]
        {
            "Assessment", "Trend", "Severity", "Risk", "Urgency", "Disposition",
            "Detected", "Matched", "Recommendation", "Advice", "Action", "Escalation",
            "Reminder", "Due", "NextCheckIn", "Provider", "Model", "Narrative"
        };

        Assert.All(entityTypes, type =>
        {
            Assert.All(type.GetProperties(), property => Assert.False(property.SetMethod?.IsPublic ?? false));
            Assert.DoesNotContain(type.GetProperties(), property =>
                forbidden.Any(value => property.Name.Contains(value, StringComparison.OrdinalIgnoreCase)));
            Assert.Empty(type.GetMethods().Where(method =>
                method.DeclaringType == type && !method.IsStatic && !method.IsSpecialName));
        });

        Assert.Equal(
            [nameof(SymptomWarningSign.Id), nameof(SymptomWarningSign.PackageVersionId),
                nameof(SymptomWarningSign.Code), nameof(SymptomWarningSign.DisplayText),
                nameof(SymptomWarningSign.SourceOrder)],
            typeof(SymptomWarningSign).GetProperties().Select(value => value.Name));
    }

    private static SymptomCheckIn CreateCheckIn(
        PreTriageEpisode episode,
        QuestionnaireDefinitionVersion questionnaire,
        SymptomDiaryPackageVersion package)
        => SymptomCheckIn.Create(
            episode,
            questionnaire,
            package,
            EntityId.New(),
            EntityId.New(),
            SymptomDiarySha256.FromHash(new string('b', 64)),
            Utc(14));

    private static SymptomDiaryPackageVersion CreatePackage(
        string packageCode = "test-diary",
        ClinicalPathwayCode? pathway = null,
        ClinicalContentStatus? status = null,
        DateTimeOffset? approvedAt = default,
        DateTimeOffset? activatedAt = default,
        bool omitApprovalTimestamp = false,
        IEnumerable<SymptomDiaryQuestionInput>? questions = null,
        IEnumerable<SymptomWarningSignInput>? warnings = null)
        => SymptomDiaryPackageVersion.Import(
            SymptomDiaryCode.Create(packageCode),
            DefinitionVersion.Create("v1"),
            SymptomDiaryCode.Create("test-question-set"),
            DefinitionVersion.Create("q1"),
            SymptomDiaryCode.Create("test-information"),
            DefinitionVersion.Create("i1"),
            pathway ?? TestPathway,
            SymptomDiarySha256.FromHash(new string('a', 64)),
            status ?? ClinicalContentStatus.MedicalTeamApproved,
            Utc(11),
            omitApprovalTimestamp ? null : approvedAt == default ? Utc(10) : approvedAt,
            activatedAt == default ? Utc(12) : activatedAt,
            "test/source/reference",
            "Synthetic information heading",
            "Synthetic information body",
            questions ?? [Question("test-question", 1)],
            warnings ?? [new(SymptomDiaryCode.Create("test-warning"), "Synthetic information", 1)]);

    private static SymptomDiaryQuestionInput Question(
        string code,
        int order,
        IReadOnlyCollection<SymptomDiaryQuestionOptionInput>? options = null)
        => new(
            SymptomDiaryCode.Create(code),
            $"Synthetic prompt {code}",
            order,
            "{\"type\":\"string\"}",
            true,
            options);

    private static SymptomDiaryQuestionOptionInput Option(string code, int order)
        => new(
            SymptomDiaryCode.Create(code),
            $"value-{code}",
            $"Synthetic option {code}",
            order);

    private static QuestionnaireDefinitionVersion CreateQuestionnaire(string code = "test-frozen")
        => QuestionnaireDefinitionVersion.Import(
            TestPathway,
            QuestionnaireCode.Create(code),
            DefinitionVersion.Create("v1"),
            DefinitionHash.FromHash(new string('d', 64)),
            ClinicalContentStatus.NonClinicalDemo,
            Utc(10));

    private static PreTriageEpisode CreatePatientOwnedEpisode(
        QuestionnaireDefinitionVersion questionnaire)
    {
        var session = PreTriageSession.CreateForPatient(
            EntityId.New(),
            questionnaire.Id,
            Utc(20),
            Utc(11));
        return PreTriageEpisode.CreateFrom(session, EntityId.New(), Utc(13));
    }

    private static ClinicalPathwayCode TestPathway { get; } =
        ClinicalPathwayCode.Create("TEST_PATHWAY");

    private static DateTimeOffset Utc(int hour)
        => new(2026, 9, 7, hour, 0, 0, TimeSpan.Zero);
}
