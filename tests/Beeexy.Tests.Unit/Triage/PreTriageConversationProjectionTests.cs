using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Triage;

public sealed class PreTriageConversationProjectionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 26, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("HEADACHE", "Headache")]
    [InlineData("ABDOMINAL_PAIN", "Stomach pain")]
    [InlineData("CHEST_PAIN", "Chest pain")]
    [InlineData("FEVER", "Fever")]
    [InlineData("OTHER_SYMPTOMS", "Other symptoms")]
    public void InitialProjection_UsesPinnedPathwayAndDurationContract(
        string pathwayCode,
        string pathwayLabel)
    {
        var package = Package(pathwayCode);
        var session = Session(package);

        var projection = PreTriageConversationProjectionBuilder.Build(
            session,
            session.Answers,
            package);

        Assert.Equal(PreTriageConversationState.InProgress, projection.State);
        Assert.Equal(PreTriageSessionStatus.Active, projection.SessionStatus);
        Assert.Equal(pathwayCode, projection.Pathway.Code.Value);
        Assert.Equal(pathwayLabel, projection.Pathway.Label);
        Assert.Equal(package.Questionnaire.Version, projection.Questionnaire.Version);
        Assert.Equal(package.RuleSet.Version, projection.RuleSet.Version);
        Assert.Equal(new PreTriageConversationProgress(0, 3, 0), projection.Progress);
        Assert.Empty(projection.AcceptedValues);
        Assert.NotNull(projection.NextInteraction);
        Assert.Equal("duration", projection.NextInteraction.Field);
        Assert.Equal("DURATION", projection.NextInteraction.QuestionCode.Value);
        Assert.Equal(PreTriageConversationInputType.Duration,
            projection.NextInteraction.InputType);
        Assert.True(projection.NextInteraction.Required);
        Assert.Equal(0, projection.NextInteraction.Constraints.Minimum);
        Assert.True(projection.NextInteraction.Constraints.ExclusiveMinimum);
        Assert.Equal(
            ["MINUTES", "HOURS", "DAYS", "WEEKS", "MONTHS"],
            projection.NextInteraction.Constraints.AllowedUnits);
        Assert.Empty(projection.NextInteraction.Options);
    }

    [Fact]
    public void AcceptedAnswers_DriveProgressAndSkipToTheNextPinnedQuestion()
    {
        var package = Package("ABDOMINAL_PAIN");
        var session = Session(package);
        Record(session, package, "DURATION", "{\"value\":2,\"unit\":\"DAYS\"}");

        var afterDuration = PreTriageConversationProjectionBuilder.Build(
            session, session.Answers, package);

        Assert.Equal(new PreTriageConversationProgress(1, 3, 33), afterDuration.Progress);
        Assert.Equal("intensity", afterDuration.NextInteraction!.Field);
        Assert.Equal(PreTriageConversationInputType.Scale,
            afterDuration.NextInteraction.InputType);
        Assert.Equal(1, afterDuration.NextInteraction.Constraints.Minimum);
        Assert.Equal(10, afterDuration.NextInteraction.Constraints.Maximum);
        Assert.Equal(1, afterDuration.NextInteraction.Constraints.Step);
        Assert.NotNull(afterDuration.AcceptedValues.Single().Value as
            ClinicalAiDurationValue);

        Record(session, package, "INTENSITY", "{\"value\":6}");
        var afterIntensity = PreTriageConversationProjectionBuilder.Build(
            session, session.Answers, package);

        Assert.Equal(new PreTriageConversationProgress(2, 3, 67), afterIntensity.Progress);
        Assert.Equal("additionalSymptoms", afterIntensity.NextInteraction!.Field);
        Assert.Equal(PreTriageConversationInputType.MultiSelect,
            afterIntensity.NextInteraction.InputType);
        Assert.Equal(0, afterIntensity.NextInteraction.Constraints.MinimumSelections);
        Assert.Equal(3, afterIntensity.NextInteraction.Constraints.MaximumSelections);
        Assert.True(afterIntensity.NextInteraction.Constraints.AllowsEmptySelection);
        Assert.Equal(
            [
                new PreTriageConversationOption("NAUSEA", "Nausea"),
                new PreTriageConversationOption("DIARRHEA", "Diarrhea"),
                new PreTriageConversationOption("FEVER", "Fever")
            ],
            afterIntensity.NextInteraction.Options);

        Record(session, package, "ADDITIONAL_SYMPTOMS", "{\"values\":[]}");
        var ready = PreTriageConversationProjectionBuilder.Build(
            session, session.Answers, package);

        Assert.Equal(PreTriageConversationState.ReadyForReview, ready.State);
        Assert.Equal(new PreTriageConversationProgress(3, 3, 100), ready.Progress);
        Assert.Null(ready.NextInteraction);
        Assert.Equal(3, ready.AcceptedValues.Count);
        Assert.Equal(PreTriageSessionStatus.Active, ready.SessionStatus);
    }

    [Fact]
    public void FeverProjection_UsesOnlyPinnedApplicableOptions()
    {
        var package = Package("FEVER");
        var session = Session(package);
        Record(session, package, "DURATION", "{\"value\":1,\"unit\":\"HOURS\"}");
        Record(session, package, "INTENSITY", "{\"value\":4}");

        var projection = PreTriageConversationProjectionBuilder.Build(
            session, session.Answers, package);

        Assert.Equal(
            [
                new PreTriageConversationOption("NAUSEA", "Nausea"),
                new PreTriageConversationOption("DIARRHEA", "Diarrhea")
            ],
            projection.NextInteraction!.Options);
        Assert.Equal(2, projection.NextInteraction.Constraints.MaximumSelections);
    }

    [Fact]
    public void CompletedProjection_IsReadOnlyAndUsesPromotedAnswers()
    {
        var package = Package("HEADACHE");
        var session = Session(package);
        Record(session, package, "DURATION", "{\"value\":1,\"unit\":\"DAYS\"}");
        Record(session, package, "INTENSITY", "{\"value\":5}");
        Record(session, package, "ADDITIONAL_SYMPTOMS", "{\"values\":[\"NAUSEA\"]}");
        var episode = PreTriageEpisode.CreateFrom(
            session,
            package.RuleSet.Id,
            Now.AddMinutes(1),
            session.ExpiresAt);

        var projection = PreTriageConversationProjectionBuilder.Build(
            session,
            episode.Answers,
            package);

        Assert.Equal(PreTriageConversationState.Completed, projection.State);
        Assert.Equal(PreTriageSessionStatus.Completed, projection.SessionStatus);
        Assert.Equal(new PreTriageConversationProgress(3, 3, 100), projection.Progress);
        Assert.Null(projection.NextInteraction);
        Assert.Equal(3, projection.AcceptedValues.Count);
    }

    [Fact]
    public void PackageFromAnotherSessionVersion_IsRejected()
    {
        var package = Package("HEADACHE");
        var session = Session(package);
        var otherPackage = Package("ABDOMINAL_PAIN");

        Assert.Throws<InvalidOperationException>(() =>
            PreTriageConversationProjectionBuilder.Build(
                session,
                session.Answers,
                otherPackage));
    }

    private static ClinicalDefinitionPackage Package(string pathway) =>
        SimplifiedDemoDefinitionPackages.Create(ClinicalPathwayCode.Create(pathway));

    private static PreTriageSession Session(ClinicalDefinitionPackage package) =>
        PreTriageSession.CreateAnonymous(
            package.Questionnaire.Id,
            AnonymousCapabilityHash.FromHash(new string('a', 64)),
            Now.AddHours(24),
            Now);

    private static void Record(
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
