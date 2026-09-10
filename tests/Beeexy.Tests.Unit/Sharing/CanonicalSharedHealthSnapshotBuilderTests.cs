using Beeexy.Application.Care;
using Beeexy.Application.History;
using Beeexy.Application.Patients;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Patients;
using Beeexy.Domain.Sharing;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;

namespace Beeexy.Tests.Unit.Sharing;

public sealed class CanonicalSharedHealthSnapshotBuilderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 9, 23, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Phase114")]
    public async Task FullProfile_MapsEveryApprovedCategoryAndDropsInternalAiContent()
    {
        var fixture = new Fixture();

        var result = await fixture.Builder.BuildAsync(
            fixture.PatientId,
            new SharedProfileSelection(ShareScope.FullProfile, true, []));

        Assert.Equal("BXY-SHARED-UNIT", result.Demographics!.BeeexyId);
        Assert.Equal("Female", result.Demographics.SexAssignedAtBirth);
        Assert.Equal(fixture.EventId, Assert.Single(result.ClinicalHistory).EventId);
        var preTriage = Assert.Single(result.PreTriage);
        Assert.Equal(fixture.EpisodeId, preTriage.EpisodeId);
        Assert.Equal("HEADACHE", preTriage.PrimarySymptom.Code);
        var checkIn = Assert.Single(result.SymptomDiaryEntries);
        Assert.Equal(fixture.CheckInId, checkIn.CheckInId);
        Assert.Equal("onset", Assert.Single(checkIn.Answers).QuestionCode);
        Assert.Equal("less-than-one-day", checkIn.Answers[0].Value[0].GetString());
        var content = Assert.Single(result.SymptomDiaryContent);
        Assert.NotEmpty(content.WarningSigns);
        Assert.Equal(fixture.SymptomPackageId, content.Package.PackageVersionId);
        var opinion = Assert.Single(result.SecondOpinions);
        Assert.Equal("Visible summary", opinion.Summary);
        Assert.Equal(["Point"], opinion.ImportantPoints);
        Assert.DoesNotContain(
            typeof(SharedSecondOpinionResult).GetProperties(),
            property => property.Name.Contains("Provider", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Model", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Prompt", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Phase114")]
    public async Task PreTriageSelection_ReturnsOnlyExactEpisodeWithoutOtherModules()
    {
        var fixture = new Fixture();

        var result = await fixture.Builder.BuildAsync(
            fixture.PatientId,
            new SharedProfileSelection(
                ShareScope.PreTriage,
                false,
                [new ShareItemReference(
                    ShareResourceType.Create(SupportedShareResourceTypes.PreTriageEpisode),
                    fixture.EpisodeId)]));

        Assert.Null(result.Demographics);
        Assert.Empty(result.ClinicalHistory);
        Assert.Equal(fixture.EpisodeId, Assert.Single(result.PreTriage).EpisodeId);
        Assert.Empty(result.SymptomDiaryEntries);
        Assert.Empty(result.SymptomDiaryContent);
        Assert.Empty(result.SecondOpinions);
    }

    [Fact]
    [Trait("Category", "Phase114")]
    public async Task SpecificRecords_ReturnsOnlyExactListedResourceAndMissingFailsClosed()
    {
        var fixture = new Fixture();
        var itemType = ShareResourceType.Create(SupportedShareResourceTypes.SymptomCheckIn);

        var result = await fixture.Builder.BuildAsync(
            fixture.PatientId,
            new SharedProfileSelection(
                ShareScope.SpecificRecords,
                false,
                [new ShareItemReference(itemType, fixture.CheckInId)]));

        Assert.Null(result.Demographics);
        Assert.Empty(result.ClinicalHistory);
        Assert.Empty(result.PreTriage);
        Assert.Equal(fixture.CheckInId, Assert.Single(result.SymptomDiaryEntries).CheckInId);
        Assert.Single(result.SymptomDiaryContent);
        Assert.Empty(result.SecondOpinions);

        await Assert.ThrowsAsync<SharedProfileSourceUnavailableException>(() =>
            fixture.Builder.BuildAsync(
                fixture.PatientId,
                new SharedProfileSelection(
                    ShareScope.SpecificRecords,
                    false,
                    [new ShareItemReference(itemType, EntityId.New())])));
        await Assert.ThrowsAsync<SharedProfileSourceUnavailableException>(() =>
            fixture.Builder.BuildAsync(
                EntityId.New(),
                new SharedProfileSelection(
                    ShareScope.SpecificRecords,
                    false,
                    [new ShareItemReference(itemType, fixture.CheckInId)])));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            PatientId = EntityId.New();
            EpisodeId = EntityId.New();
            EventId = EntityId.New();
            CheckInId = EntityId.New();
            var questionnaireId = EntityId.New();
            var ruleSetId = EntityId.New();
            var item = new ClinicalHistoryListItem(
                EventId,
                ClinicalHistoryEventType.CompletedPreTriage,
                Now.AddHours(-2),
                Now.AddHours(-2),
                AuthoritativeClinicalSourceType.PreTriageEpisode,
                EpisodeId,
                questionnaireId,
                ruleSetId);
            var detail = new ClinicalHistoryEventDetail(
                item,
                new ClinicalHistorySourceDetail(
                    AuthoritativeClinicalSourceType.PreTriageEpisode,
                    EpisodeId,
                    Now.AddHours(-2),
                    questionnaireId,
                    ruleSetId),
                [],
                new CompletedPreTriageSummary(
                    new CompletedPreTriagePrimarySymptom("HEADACHE", "Headache"),
                    new CompletedPreTriageDuration(2, "days"),
                    4,
                    ["NAUSEA"]));
            var clinical = new ClinicalRepository(item, detail);

            var definition = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
            var package = new SymptomDiaryPackageContent(
                EntityId.New(),
                definition.ExpectedContentHash!,
                definition);
            SymptomPackageId = package.PackageVersionId;
            var question = definition.Questions[0];
            var symptomRecord = new SymptomCheckInHistoryRecord(
                CheckInId,
                EpisodeId,
                package.PackageVersionId,
                Now.AddHours(-1),
                [new SymptomCheckInHistoryAnswerRecord(
                    question.Code,
                    question.SourceOrder,
                    "[\"less-than-one-day\"]")]);
            var symptom = new SymptomRepository(
                new EligibleSymptomDiaryEpisode(
                    EpisodeId,
                    PatientId,
                    ClinicalPathways.Headache),
                symptomRecord,
                package);
            var opinions = new SecondOpinionRepository(new SharedSecondOpinionStoredResult(
                EntityId.New(),
                EntityId.New(),
                Now,
                "ai-second-opinion-result@v1",
                """
                {
                  "summary":"Visible summary",
                  "importantPoints":["Point"],
                  "possibleQuestionsForDoctor":["Question"],
                  "missingInformation":["Missing"],
                  "provider":"must-not-project",
                  "rawProviderOutput":"must-not-project"
                }
                """));

            Builder = new CanonicalSharedHealthSnapshotBuilder(
                new PatientRepository(new PatientProfileReadRecord(
                    PatientId,
                    "BXY-SHARED-UNIT",
                    "Ana",
                    "Patient",
                    new DateOnly(1990, 1, 2),
                    SexAssignedAtBirth.Female,
                    "CA",
                    3)),
                clinical,
                clinical,
                symptom,
                symptom,
                symptom,
                new SymptomDiaryPackageValidator(),
                opinions);
        }

        public EntityId PatientId { get; }
        public EntityId EpisodeId { get; }
        public EntityId EventId { get; }
        public EntityId CheckInId { get; }
        public EntityId SymptomPackageId { get; }
        public CanonicalSharedHealthSnapshotBuilder Builder { get; }
    }

    private sealed class PatientRepository(PatientProfileReadRecord record)
        : IPatientProfileReadRepository
    {
        public Task<PatientProfileReadRecord?> FindAsync(
            EntityId profileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PatientProfileReadRecord?>(
                profileId == record.ProfileId ? record : null);
    }

    private sealed class ClinicalRepository(
        ClinicalHistoryListItem item,
        ClinicalHistoryEventDetail detail) :
        IClinicalHistoryReadRepository,
        IClinicalHistoryEventReadRepository
    {
        public Task<bool> CursorExistsAsync(
            ClinicalHistoryPageCursor cursor,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyList<ClinicalHistoryListItem>> ListAsync(
            EntityId patientProfileId,
            ClinicalHistoryEventType? eventType,
            ClinicalHistoryPageCursor? after,
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClinicalHistoryListItem>>(
                after is null ? [item] : []);

        public Task<ClinicalHistoryEventDetail?> GetAsync(
            EntityId patientProfileId,
            EntityId eventId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ClinicalHistoryEventDetail?>(
                eventId == item.EventId ? detail : null);

        public Task<ClinicalHistoryEventDetail?> GetByPreTriageEpisodeAsync(
            EntityId patientProfileId,
            EntityId episodeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ClinicalHistoryEventDetail?>(
                episodeId == detail.AuthoritativeSource.Id ? detail : null);
    }

    private sealed class SymptomRepository(
        EligibleSymptomDiaryEpisode episode,
        SymptomCheckInHistoryRecord record,
        SymptomDiaryPackageContent package) :
        ISymptomDiaryEpisodeReadRepository,
        ISymptomCheckInReadRepository,
        ISymptomDiaryExactContentBatchProvider
    {
        public Task<EligibleSymptomDiaryEpisode?> GetEligibleAsync(
            EntityId episodeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EligibleSymptomDiaryEpisode?>(
                episodeId == episode.EpisodeId ? episode : null);

        public Task<IReadOnlyList<EligibleSymptomDiaryEpisode>> ListEligibleAsync(
            EntityId patientProfileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EligibleSymptomDiaryEpisode>>(
                patientProfileId == episode.PatientProfileId ? [episode] : []);

        public Task<bool> CursorExistsAsync(
            SymptomCheckInPageCursor cursor,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyList<SymptomCheckInHistoryRecord>> ListAsync(
            EntityId episodeId,
            SymptomCheckInPageCursor? after,
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SymptomCheckInHistoryRecord>>(
                episodeId == episode.EpisodeId && after is null ? [record] : []);

        public Task<SymptomCheckInHistoryRecord?> GetAsync(
            EntityId checkInId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SymptomCheckInHistoryRecord?>(
                checkInId == record.CheckInId ? record : null);

        public Task<IReadOnlyDictionary<EntityId, SymptomDiaryPackageContent>>
            GetExactPackagesAsync(
                IReadOnlyCollection<EntityId> packageVersionIds,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<EntityId, SymptomDiaryPackageContent>>(
                packageVersionIds.Contains(package.PackageVersionId)
                    ? new Dictionary<EntityId, SymptomDiaryPackageContent>
                    {
                        [package.PackageVersionId] = package
                    }
                    : new Dictionary<EntityId, SymptomDiaryPackageContent>());
    }

    private sealed class SecondOpinionRepository(SharedSecondOpinionStoredResult result)
        : ISharedSecondOpinionReadRepository
    {
        public Task<IReadOnlyList<SharedSecondOpinionStoredResult>> ListDisplayableAsync(
            EntityId patientProfileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SharedSecondOpinionStoredResult>>([result]);

        public Task<SharedSecondOpinionStoredResult?> GetDisplayableAsync(
            EntityId patientProfileId,
            EntityId resultId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SharedSecondOpinionStoredResult?>(
                resultId == result.ResultId ? result : null);
    }
}
