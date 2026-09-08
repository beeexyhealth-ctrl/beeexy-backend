using System.Text.Json;
using Beeexy.Application.Care;
using Beeexy.Application.Patients;
using Beeexy.Application.Triage;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Care;

[Trait("Category", "Phase95")]
public sealed class RecordSymptomCheckInTests
{
    [Theory]
    [InlineData("HEADACHE")]
    [InlineData("ABDOMINAL_PAIN")]
    [InlineData("FEVER")]
    [InlineData("CHEST_PAIN")]
    public async Task ValidExactPackageCreatesNeutralEntryForTrustedEpisodePathway(
        string pathwayValue)
    {
        var fixture = new Fixture(ClinicalPathwayCode.Create(pathwayValue));
        var answer = fixture.ValidAnswer();

        var result = await fixture.ExecuteAsync([answer]);

        Assert.True(result.NewlyCreated);
        Assert.Equal(fixture.Source.Pathway, result.Pathway);
        Assert.Equal(fixture.Package.Id, result.PackageVersionId);
        Assert.Equal(Fixture.Now, result.CreatedAt);
        Assert.NotNull(fixture.Transaction.Added);
        Assert.Equal(fixture.Source.Episode.Id, fixture.Transaction.Added.EpisodeId);
        Assert.Equal(fixture.Profiles.Account.Id,
            fixture.Transaction.Added.SubmittingAccountId);
        Assert.Single(fixture.Transaction.Added.Answers);
        Assert.Equal(answer.Value.GetRawText(),
            fixture.Transaction.Added.Answers.Single().SubmittedValueJson);
        Assert.Equal(fixture.Package.Id, fixture.ContentProvider.ExactRequested);
        Assert.Null(fixture.ContentProvider.ActiveRequested);
    }

    [Fact]
    public async Task PreviouslyPresentedReviewedApprovedActivatedVersionRemainsExactBinding()
    {
        var fixture = new Fixture(ClinicalPathways.Headache);

        var result = await fixture.ExecuteAsync([fixture.ValidAnswer()]);

        Assert.Equal(fixture.Package.Id, result.PackageVersionId);
        Assert.Equal(fixture.Package.CanonicalContentHash, result.Package.CanonicalContentHash);
        Assert.Null(fixture.ContentProvider.ActiveRequested);
    }

    [Fact]
    public async Task UnknownOrWrongPathwayPackageFailsWithoutPersistence()
    {
        var fixture = new Fixture(ClinicalPathways.Headache);
        fixture.ContentProvider.ExactResult = null;
        await Assert.ThrowsAsync<SymptomDiaryContentUnavailableException>(() =>
            fixture.ExecuteAsync([fixture.ValidAnswer()]));
        Assert.Null(fixture.Transaction.Added);

        fixture.ContentProvider.ExactResult = new SymptomDiaryPackageContent(
            fixture.Package.Id,
            fixture.Package.CanonicalContentHash,
            AndreaSymptomDiaryPackages.Create(ClinicalPathways.Fever));
        await Assert.ThrowsAsync<SymptomDiaryContentUnavailableException>(() =>
            fixture.ExecuteAsync([fixture.ValidAnswer()]));
        Assert.Null(fixture.Transaction.Added);
    }

    [Fact]
    public async Task IdenticalRetryReturnsOriginalEntryAndDoesNotAddAnother()
    {
        var fixture = new Fixture(ClinicalPathways.Headache);
        var answer = fixture.ValidAnswer();
        var validated = new SymptomDiaryAnswerStructureValidator().Validate(
            fixture.Content.Definition,
            [answer]);
        fixture.Transaction.Existing = SymptomCheckIn.Create(
            fixture.Source.Episode,
            fixture.Source.FrozenQuestionnaire,
            fixture.Package,
            fixture.Profiles.Account.Id,
            fixture.IdempotencyKey,
            SymptomCheckInRequestHashCalculator.Calculate(fixture.Package.Id, validated),
            NowMinusMinute());

        var result = await fixture.ExecuteAsync([answer]);

        Assert.False(result.NewlyCreated);
        Assert.Equal(fixture.Transaction.Existing.Id, result.CheckInId);
        Assert.Equal(NowMinusMinute(), result.CreatedAt);
        Assert.Null(fixture.Transaction.Added);
    }

    [Fact]
    public async Task ChangedPayloadUnderSameKeyConflictsAndPreservesOriginal()
    {
        var fixture = new Fixture(ClinicalPathways.Headache);
        var answer = fixture.ValidAnswer();
        fixture.Transaction.Existing = SymptomCheckIn.Create(
            fixture.Source.Episode,
            fixture.Source.FrozenQuestionnaire,
            fixture.Package,
            fixture.Profiles.Account.Id,
            fixture.IdempotencyKey,
            SymptomDiarySha256.FromHash(new string('a', 64)),
            NowMinusMinute());

        await Assert.ThrowsAsync<SymptomCheckInIdempotencyConflictException>(() =>
            fixture.ExecuteAsync([answer]));

        Assert.Null(fixture.Transaction.Added);
        Assert.Equal(1, fixture.Audit.ConflictCount);
    }

    [Fact]
    public async Task IneligibleOrUnauthorizedEpisodeIsConcealedBeforePackageValidation()
    {
        var fixture = new Fixture(ClinicalPathways.Headache);
        fixture.EpisodeRepository.Result = null;
        await Assert.ThrowsAsync<SymptomDiaryEpisodeNotFoundException>(() =>
            fixture.ExecuteAsync([fixture.ValidAnswer()]));
        Assert.Null(fixture.ContentProvider.ExactRequested);

        fixture.EpisodeRepository.Result = new EligibleSymptomDiaryEpisode(
            fixture.Source.Episode.Id,
            EntityId.New(),
            fixture.Source.Pathway);
        await Assert.ThrowsAsync<SymptomDiaryEpisodeNotFoundException>(() =>
            fixture.ExecuteAsync([fixture.ValidAnswer()]));
        Assert.Null(fixture.ContentProvider.ExactRequested);
    }

    private static DateTimeOffset NowMinusMinute() =>
        Fixture.Now.AddMinutes(-1);

    private sealed class Fixture
    {
        public static readonly DateTimeOffset Now =
            new(2026, 9, 7, 18, 0, 0, TimeSpan.Zero);

        public Fixture(ClinicalPathwayCode pathway)
        {
            var triage = SimplifiedDemoDefinitionPackages.Create(pathway);
            var session = PreTriageSession.CreateForPatient(
                Profiles.PrimaryProfile.Id,
                triage.Questionnaire.Id,
                Now.AddDays(1),
                Now.AddHours(-1));
            var episode = PreTriageEpisode.CreateFrom(
                session,
                triage.RuleSet.Id,
                Now.AddMinutes(-30));
            Source = new SymptomCheckInEpisodeSource(episode, triage.Questionnaire);
            EpisodeRepository.Result = new EligibleSymptomDiaryEpisode(
                episode.Id,
                Profiles.PrimaryProfile.Id,
                pathway);

            var definition = AndreaSymptomDiaryPackages.Create(pathway);
            Package = SymptomDiaryPackageMapper.ToEntity(
                definition,
                definition.ExpectedContentHash!);
            Content = SymptomDiaryPackageMapper.ToContent(Package);
            ContentProvider.ExactResult = Content;
            Transaction.Source = Source;
            Transaction.Package = Package;

            var authorization = new AuthorizePatientAccess(
                new FakeClock(),
                Profiles.Resolver,
                AuthorizationRepository,
                Profiles.MyCircleAudit);
            UseCase = new RecordSymptomCheckIn(
                new FakeClock(),
                Profiles.Resolver,
                authorization,
                EpisodeRepository,
                ContentProvider,
                new SymptomDiaryPackageValidator(),
                new SymptomDiaryAnswerStructureValidator(),
                Transaction,
                Audit);
        }

        public MyCircleListingTestFixture Profiles { get; } = new();
        public FakeAuthorizationRepository AuthorizationRepository { get; } = new();
        public FakeEpisodeRepository EpisodeRepository { get; } = new();
        public FakeContentProvider ContentProvider { get; } = new();
        public FakeTransaction Transaction { get; } = new();
        public FakeAuditLogger Audit { get; } = new();
        public SymptomCheckInEpisodeSource Source { get; }
        public SymptomDiaryPackageVersion Package { get; }
        public SymptomDiaryPackageContent Content { get; }
        public EntityId IdempotencyKey { get; } = EntityId.New();
        public RecordSymptomCheckIn UseCase { get; }

        public SymptomDiarySubmittedAnswer ValidAnswer()
        {
            var question = Content.Definition.Questions[0];
            using var schema = JsonDocument.Parse(question.AnswerSchemaJson);
            var value = question.Options.Count > 0
                ? JsonSerializer.SerializeToElement(question.Options[0].Value)
                : JsonSerializer.SerializeToElement("Texto exacto");
            if (schema.RootElement.GetProperty("type").GetString() == "array")
            {
                value = JsonSerializer.SerializeToElement(
                    new[] { question.Options[0].Value });
            }

            return new SymptomDiarySubmittedAnswer(question.Code.Value, value);
        }

        public Task<RecordSymptomCheckInResult> ExecuteAsync(
            IReadOnlyList<SymptomDiarySubmittedAnswer> answers) =>
            UseCase.ExecuteAsync(new RecordSymptomCheckInCommand(
                Source.Episode.Id,
                Package.Id,
                IdempotencyKey,
                answers));
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Fixture.Now;
    }

    private sealed class FakeEpisodeRepository : ISymptomDiaryEpisodeReadRepository
    {
        public EligibleSymptomDiaryEpisode? Result { get; set; }

        public Task<EligibleSymptomDiaryEpisode?> GetEligibleAsync(
            EntityId episodeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result?.EpisodeId == episodeId ? Result : null);
    }

    private sealed class FakeContentProvider : ISymptomDiaryContentProvider
    {
        public SymptomDiaryPackageContent? ExactResult { get; set; }
        public EntityId? ExactRequested { get; private set; }
        public ClinicalPathwayCode? ActiveRequested { get; private set; }

        public Task<SymptomDiaryPackageContent?> GetActivePackageAsync(
            ClinicalPathwayCode pathway,
            CancellationToken cancellationToken = default)
        {
            ActiveRequested = pathway;
            return Task.FromResult<SymptomDiaryPackageContent?>(null);
        }

        public Task<SymptomDiaryPackageContent?> GetExactPackageAsync(
            EntityId packageVersionId,
            CancellationToken cancellationToken = default)
        {
            ExactRequested = packageVersionId;
            return Task.FromResult(ExactResult);
        }
    }

    private sealed class FakeAuthorizationRepository : IPatientAccessAuthorizationRepository
    {
        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PatientAccessAuthorizationLookup(false, null));

        public Task<PatientAccessAuthorizationLookup> FindForPatientUpdateAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default) =>
            FindAsync(managerProfileId, targetProfileId, cancellationToken);
    }

    private sealed class FakeTransaction : ISymptomCheckInTransaction
    {
        public SymptomCheckInEpisodeSource? Source { get; set; }
        public SymptomDiaryPackageVersion? Package { get; set; }
        public SymptomCheckIn? Existing { get; set; }
        public SymptomCheckIn? Added { get; private set; }

        public Task BeginAsync(EntityId episodeId, EntityId idempotencyKey,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SymptomCheckInEpisodeSource?> FindEpisodeAsync(EntityId episodeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Source?.Episode.Id == episodeId ? Source : null);

        public Task<SymptomCheckIn?> FindExistingAsync(EntityId episodeId,
            EntityId idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(Existing);

        public Task<SymptomDiaryPackageVersion?> FindPackageAsync(EntityId packageVersionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Package?.Id == packageVersionId ? Package : null);

        public void Add(SymptomCheckIn checkIn) => Added = checkIn;

        public Task<SymptomCheckInSaveResult> SaveAsync(SymptomCheckIn checkIn,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SymptomCheckInSaveResult(checkIn, true));

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAuditLogger : ISymptomCheckInAuditLogger
    {
        public int ConflictCount { get; private set; }

        public void Recorded(EntityId actorAccountId, EntityId episodeId,
            EntityId packageVersionId, EntityId checkInId, int answerCount,
            PatientAccessReason accessReason, bool newlyCreated, DateTimeOffset createdAt)
        {
        }

        public void IdempotencyConflict(EntityId episodeId, EntityId packageVersionId,
            EntityId idempotencyKey, DateTimeOffset rejectedAt) => ConflictCount++;
    }
}
