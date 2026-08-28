using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Triage;

public sealed class PreTriageConversationProjectionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 26, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OtherSymptoms_UsesPinnedPathwayAndDurationContractWithoutVideoOffer()
    {
        const string pathwayCode = "OTHER_SYMPTOMS";
        const string pathwayLabel = "Other symptoms";
        var package = Package(pathwayCode);
        var session = Session(package);

        var projection = PreTriageConversationProjectionBuilder.Build(
            session,
            session.Answers,
            package,
            Catalog());

        Assert.Equal(PreTriageConversationState.InProgress, projection.State);
        Assert.Equal(PreTriageSessionStatus.Active, projection.SessionStatus);
        Assert.Equal(pathwayCode, projection.Pathway.Code.Value);
        Assert.Equal(pathwayLabel, projection.Pathway.Label);
        Assert.Equal(package.Questionnaire.Version, projection.Questionnaire.Version);
        Assert.Equal(package.RuleSet.Version, projection.RuleSet.Version);
        Assert.Equal(new PreTriageConversationProgress(0, 3, 0), projection.Progress);
        Assert.Empty(projection.AcceptedValues);
        Assert.NotNull(projection.NextInteraction);
        Assert.Equal(PreTriageConversationInteractionType.Question,
            projection.NextInteraction.Type);
        Assert.Equal("duration", projection.NextInteraction.Field);
        Assert.Equal("DURATION", projection.NextInteraction.QuestionCode!.Value);
        Assert.Equal(PreTriageConversationInputType.Duration,
            projection.NextInteraction.InputType);
        Assert.True(projection.NextInteraction.Required);
        Assert.Equal(0, projection.NextInteraction.Constraints.Minimum);
        Assert.True(projection.NextInteraction.Constraints.ExclusiveMinimum);
        Assert.Equal(
            ["MINUTES", "HOURS", "DAYS", "WEEKS", "MONTHS"],
            projection.NextInteraction.Constraints.AllowedUnits);
        Assert.Empty(projection.NextInteraction.Options);
        Assert.Null(projection.NextInteraction.Video);
    }

    [Theory]
    [InlineData("HEADACHE", "headache", "Understanding Headaches")]
    [InlineData("ABDOMINAL_PAIN", "abdominal-pain", "Understanding Stomach Pain")]
    [InlineData("CHEST_PAIN", "chest-pain", "Understanding Chest Pain")]
    [InlineData("FEVER", "fever", "Understanding Fever")]
    public void ConfiguredPathway_StartsWithEducationalVideoOffer(
        string pathwayCode,
        string videoId,
        string videoTitle)
    {
        var package = Package(pathwayCode);
        var session = Session(package, educationalVideoOfferRequired: true);

        var projection = PreTriageConversationProjectionBuilder.Build(
            session,
            session.Answers,
            package,
            Catalog());

        Assert.Equal(PreTriageConversationState.InProgress, projection.State);
        Assert.Equal(new PreTriageConversationProgress(0, 3, 0), projection.Progress);
        Assert.Empty(projection.AcceptedValues);
        var interaction = Assert.IsType<PreTriageConversationInteraction>(
            projection.NextInteraction);
        Assert.Equal(PreTriageConversationInteractionType.EducationalVideoOffer,
            interaction.Type);
        Assert.Equal("educationalVideoDecision", interaction.Field);
        Assert.Null(interaction.QuestionCode);
        Assert.Equal(PreTriageConversationInputType.SingleSelect, interaction.InputType);
        Assert.False(interaction.Required);
        Assert.Equal(
            [
                new PreTriageConversationOption("WATCH", "Yes, show me the video"),
                new PreTriageConversationOption("SKIP", "No, continue with assessment")
            ],
            interaction.Options);
        Assert.Equal(videoId, interaction.Video!.Id);
        Assert.Equal(videoTitle, interaction.Video.Title);
        Assert.StartsWith("https://res.cloudinary.com/", interaction.Video.Url);
    }

    [Theory]
    [InlineData(PreTriageEducationalVideoDecision.Watch)]
    [InlineData(PreTriageEducationalVideoDecision.Skip)]
    public void ResolvingOffer_AdvancesToSameClinicalQuestionWithoutClinicalValues(
        PreTriageEducationalVideoDecision decision)
    {
        var package = Package("HEADACHE");
        var session = Session(package, educationalVideoOfferRequired: true);

        Assert.True(session.ResolveEducationalVideoOffer(decision, Now.AddMinutes(1)));
        Assert.False(session.ResolveEducationalVideoOffer(decision, Now.AddMinutes(2)));
        var projection = PreTriageConversationProjectionBuilder.Build(
            session, session.Answers, package, Catalog());

        Assert.Empty(projection.AcceptedValues);
        Assert.Equal(new PreTriageConversationProgress(0, 3, 0), projection.Progress);
        Assert.Equal(PreTriageConversationInteractionType.Question,
            projection.NextInteraction!.Type);
        Assert.Equal("DURATION", projection.NextInteraction.QuestionCode!.Value);
        Assert.Equal("duration", projection.NextInteraction.Field);
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

    private static PreTriageSession Session(
        ClinicalDefinitionPackage package,
        bool educationalVideoOfferRequired = false) =>
        PreTriageSession.CreateAnonymous(
            package.Questionnaire.Id,
            AnonymousCapabilityHash.FromHash(new string('a', 64)),
            Now.AddHours(24),
            Now,
            educationalVideoOfferRequired);

    private static IPreTriageEducationalVideoCatalog Catalog() =>
        new FixedEducationalVideoCatalog();

    private sealed class FixedEducationalVideoCatalog : IPreTriageEducationalVideoCatalog
    {
        public PreTriageEducationalVideo? Find(ClinicalPathwayCode pathway) =>
            pathway.Value switch
            {
                "HEADACHE" => Video("headache", "Understanding Headaches"),
                "ABDOMINAL_PAIN" => Video(
                    "abdominal-pain", "Understanding Stomach Pain"),
                "CHEST_PAIN" => Video("chest-pain", "Understanding Chest Pain"),
                "FEVER" => Video("fever", "Understanding Fever"),
                _ => null
            };

        private static PreTriageEducationalVideo Video(string id, string title) => new(
            id,
            title,
            $"https://res.cloudinary.com/example/video/upload/{id}.mp4");
    }

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
