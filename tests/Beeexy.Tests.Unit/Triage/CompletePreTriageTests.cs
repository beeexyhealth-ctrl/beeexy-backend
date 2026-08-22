using Beeexy.Application.Common;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Triage;

public sealed class CompletePreTriageTests
{
    [Theory]
    [InlineData("HEADACHE", "Headache", "[\"FEVER\"]")]
    [InlineData("ABDOMINAL_PAIN", "Stomach pain", "[\"NAUSEA\",\"FEVER\"]")]
    [InlineData("FEVER", "Fever", "[\"NAUSEA\",\"DIARRHEA\"]")]
    public void Completeness_UsesExactPinnedDemoPackage(
        string pathwayCode,
        string display,
        string additionalJson)
    {
        var package = SimplifiedDemoDefinitionPackages.Create(
            ClinicalPathwayCode.Create(pathwayCode));
        var session = CompleteSession(package, additionalJson);

        var result = new CheckDemoQuestionnaireCompleteness()
            .CheckTemporary(session, package);

        Assert.Equal(display, result.PrimarySymptomDisplay);
        Assert.Equal(2, result.DurationValue);
        Assert.Equal("DAYS", result.DurationUnit);
        Assert.Equal(7, result.Intensity);
        Assert.DoesNotContain(
            result.AdditionalSymptoms,
            value => pathwayCode == "FEVER" && value == "FEVER");
    }

    [Fact]
    public void Completeness_RejectsMissingAndMalformedAnswers()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var missing = CreateSession(package);
        AddAnswer(missing, package, "DURATION", "{\"value\":2,\"unit\":\"DAYS\"}");
        var malformed = CreateSession(package);
        AddAnswer(malformed, package, "DURATION",
            "{\"value\":2,\"unit\":\"DAYS\",\"urgency\":\"HIGH\"}");
        AddAnswer(malformed, package, "INTENSITY", "{\"value\":7}");
        AddAnswer(malformed, package, "ADDITIONAL_SYMPTOMS", "{\"values\":[]}");
        var policy = new CheckDemoQuestionnaireCompleteness();

        var missingError = Assert.Throws<RequestValidationException>(() =>
            policy.CheckTemporary(missing, package));
        var malformedError = Assert.Throws<RequestValidationException>(() =>
            policy.CheckTemporary(malformed, package));

        Assert.Equal("pre_triage.completion_incomplete", missingError.Code);
        Assert.Equal("pre_triage.completion_incomplete", malformedError.Code);
    }

    [Fact]
    public void FeverCompleteness_RejectsCorruptRedundantFeverState()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Fever);
        var session = CompleteSession(package, "[\"FEVER\"]");

        var error = Assert.Throws<RequestValidationException>(() =>
            new CheckDemoQuestionnaireCompleteness().CheckTemporary(session, package));

        Assert.Equal("pre_triage.completion_incomplete", error.Code);
    }

    [Fact]
    public void NeutralFactory_CreatesNoUrgencyMessageOrFindings()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var session = CompleteSession(package, "[]");
        var completedAt = Now.AddMinutes(4);
        var episode = PreTriageEpisode.CreateFrom(
            session,
            package.RuleSet.Id,
            completedAt,
            session.ExpiresAt);

        var assessment = new NeutralClinicalAssessmentFactory()
            .Create(episode, completedAt);

        Assert.Null(assessment.UrgencyCode);
        Assert.Null(assessment.ResultMessageReference);
        Assert.Empty(assessment.Findings);
        Assert.Equal(package.RuleSet.Id, assessment.ClinicalRuleSetVersionId);
    }

    private static readonly DateTimeOffset Now =
        new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private static PreTriageSession CompleteSession(
        ClinicalDefinitionPackage package,
        string additionalJson)
    {
        var session = CreateSession(package);
        AddAnswer(session, package, "DURATION", "{\"value\":2,\"unit\":\"DAYS\"}");
        AddAnswer(session, package, "INTENSITY", "{\"value\":7}");
        AddAnswer(session, package, "ADDITIONAL_SYMPTOMS",
            $"{{\"values\":{additionalJson}}}");
        return session;
    }

    private static PreTriageSession CreateSession(ClinicalDefinitionPackage package) =>
        PreTriageSession.CreateAnonymous(
            package.Questionnaire.Id,
            AnonymousCapabilityHash.FromHash(new string('a', 64)),
            Now.AddHours(24),
            Now);

    private static void AddAnswer(
        PreTriageSession session,
        ClinicalDefinitionPackage package,
        string code,
        string json)
    {
        var question = package.Questionnaire.Questions.Single(
            value => value.Code == QuestionCode.Create(code));
        session.RecordAnswer(question, json, question.DisplayOrder, Now.AddMinutes(1));
    }
}
