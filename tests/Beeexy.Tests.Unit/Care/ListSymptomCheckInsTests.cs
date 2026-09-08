using Beeexy.Application.Care;
using Beeexy.Application.Patients;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Care;

[Trait("Category", "Phase96")]
public sealed class ListSymptomCheckInsTests
{
    [Fact]
    public async Task EmptyHistoryReturnsStableEmptyPageWithDefaultLimit()
    {
        var fixture = new Fixture();

        var result = await fixture.ExecuteAsync();

        Assert.Empty(result.Items);
        Assert.Null(result.NextCursor);
        Assert.Equal(ListSymptomCheckIns.DefaultPageSize + 1, fixture.Repository.LastTake);
        Assert.Empty(fixture.Provider.RequestedIds);
    }

    [Fact]
    public async Task SingleEntryPreservesFrozenContentSubmittedJsonHashAndProvenance()
    {
        var fixture = new Fixture();
        var record = fixture.Record(1, "[\"A\",\"B\"]");
        fixture.Repository.Results = [record];

        var result = await fixture.ExecuteAsync();

        var item = Assert.Single(result.Items);
        Assert.Equal(record.CheckInId, item.CheckInId);
        Assert.Equal(record.CreatedAt, item.CreatedAt);
        Assert.Same(fixture.Content, item.Package);
        Assert.Equal(fixture.Content.CanonicalContentHash, item.Package.CanonicalContentHash);
        Assert.Equal(fixture.Content.Definition.ContentStatus, item.Package.Definition.ContentStatus);
        Assert.Equal(record.Answers[0].SubmittedValueJson,
            item.Answers[0].Value.GetRawText());
        Assert.Equal(fixture.Content.Definition.Questions[0], item.Answers[0].Question);
        Assert.Equal(fixture.Content.Definition.WarningSigns, item.Package.Definition.WarningSigns);
    }

    [Fact]
    public async Task ChronologicalPagesTraverseEveryEntryOnceIncludingTiedTimes()
    {
        var fixture = new Fixture();
        fixture.Repository.Results = [
            fixture.Record(1), fixture.Record(2, createdAtOffset: 0),
            fixture.Record(3, createdAtOffset: 1), fixture.Record(4, createdAtOffset: 2),
            fixture.Record(5, createdAtOffset: 3)];
        var seen = new List<EntityId>();
        string? cursor = null;

        do
        {
            var page = await fixture.ExecuteAsync(cursor, 2);
            seen.AddRange(page.Items.Select(item => item.CheckInId));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(fixture.Repository.Results.Select(item => item.CheckInId), seen);
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-1)]
    public async Task InvalidPageSizeFailsBeforeHistoryRead(int pageSize)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<Beeexy.Application.Common.RequestValidationException>(
            () => fixture.ExecuteAsync(pageSize: pageSize));

        Assert.Equal("symptom_diary.page_size_invalid", exception.Code);
        Assert.Equal(0, fixture.Repository.ListCalls);
    }

    [Fact]
    public async Task MaximumPageSizeIsBoundedToOneHundredPlusLookahead()
    {
        var fixture = new Fixture();

        await fixture.ExecuteAsync(pageSize: 100);

        Assert.Equal(101, fixture.Repository.LastTake);
    }

    [Fact]
    public async Task CursorIsOpaqueDeterministicAndBoundToEpisodeAndNormalizedSize()
    {
        var fixture = new Fixture();
        fixture.Repository.Results = [fixture.Record(1), fixture.Record(2)];
        var first = await fixture.ExecuteAsync(pageSize: 1);
        var repeated = await fixture.ExecuteAsync(pageSize: 1);
        Assert.Equal(first.NextCursor, repeated.NextCursor);
        Assert.DoesNotContain(fixture.Episode.EpisodeId.Value.ToString("D"), first.NextCursor!);

        var otherEpisode = EntityId.New();
        var crossEpisode = Assert.Throws<Beeexy.Application.Common.RequestValidationException>(() =>
            fixture.Cursor.Decode(first.NextCursor!, otherEpisode, 1));
        var sizeMismatch = Assert.Throws<Beeexy.Application.Common.RequestValidationException>(() =>
            fixture.Cursor.Decode(first.NextCursor!, fixture.Episode.EpisodeId, 2));

        Assert.Equal("symptom_diary.cursor_invalid", crossEpisode.Code);
        Assert.Equal("symptom_diary.cursor_invalid", sizeMismatch.Code);
    }

    [Fact]
    public async Task MalformedTamperedAndStaleCursorFailClosed()
    {
        var fixture = new Fixture();
        fixture.Repository.Results = [fixture.Record(1), fixture.Record(2)];
        var first = await fixture.ExecuteAsync(pageSize: 1);
        var tampered = first.NextCursor![..^1] +
            (first.NextCursor[^1] == 'A' ? 'B' : 'A');

        var malformed = await Assert.ThrowsAsync<Beeexy.Application.Common.RequestValidationException>(
            () => fixture.ExecuteAsync("not+a+cursor", 1));
        var altered = await Assert.ThrowsAsync<Beeexy.Application.Common.RequestValidationException>(
            () => fixture.ExecuteAsync(tampered, 1));
        fixture.Repository.CursorExists = false;
        var stale = await Assert.ThrowsAsync<Beeexy.Application.Common.RequestValidationException>(
            () => fixture.ExecuteAsync(first.NextCursor, 1));

        Assert.All([malformed, altered, stale], failure =>
            Assert.Equal("symptom_diary.cursor_invalid", failure.Code));
    }

    [Fact]
    public async Task MissingCrossPathwayOrStructurallyInconsistentFrozenContentFailsClosed()
    {
        var fixture = new Fixture();
        fixture.Repository.Results = [fixture.Record(1)];
        fixture.Provider.Packages.Clear();
        await Assert.ThrowsAsync<SymptomDiaryContentUnavailableException>(() =>
            fixture.ExecuteAsync());

        fixture.Provider.Packages[fixture.Content.PackageVersionId] =
            fixture.Content with
            {
                Definition = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Fever)
            };
        await Assert.ThrowsAsync<SymptomDiaryContentUnavailableException>(() =>
            fixture.ExecuteAsync());

        fixture.Provider.Packages[fixture.Content.PackageVersionId] = fixture.Content;
        fixture.Repository.Results = [fixture.Record(1) with
        {
            Answers = [fixture.Record(1).Answers[0] with { SourceOrder = 999 }]
        }];
        await Assert.ThrowsAsync<SymptomDiaryContentUnavailableException>(() =>
            fixture.ExecuteAsync());
    }

    [Fact]
    public async Task IneligibleOrUnauthorizedEpisodeIsConcealedBeforeCursorAndRepositoryWork()
    {
        var fixture = new Fixture();
        fixture.EpisodeRepository.Result = null;
        await Assert.ThrowsAsync<SymptomDiaryEpisodeNotFoundException>(() =>
            fixture.ExecuteAsync("invalid"));
        Assert.Equal(0, fixture.Repository.ListCalls);

        fixture.EpisodeRepository.Result = fixture.Episode with
        {
            PatientProfileId = EntityId.New()
        };
        await Assert.ThrowsAsync<SymptomDiaryEpisodeNotFoundException>(() =>
            fixture.ExecuteAsync("invalid"));
        Assert.Equal(0, fixture.Repository.ListCalls);
    }

    private sealed class Fixture
    {
        private static readonly DateTimeOffset Time =
            new(2026, 9, 7, 18, 0, 0, TimeSpan.Zero);

        public Fixture()
        {
            var profiles = new MyCircleListingTestFixture();
            Episode = new EligibleSymptomDiaryEpisode(
                EntityId.New(), profiles.PrimaryProfile.Id, ClinicalPathways.Headache);
            EpisodeRepository.Result = Episode;
            var definition = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
            Content = new SymptomDiaryPackageContent(
                EntityId.New(), definition.ExpectedContentHash!, definition);
            Provider.Packages[Content.PackageVersionId] = Content;
            var authorizer = new AuthorizePatientAccess(
                new FakeClock(),
                profiles.Resolver,
                AuthorizationRepository,
                profiles.MyCircleAudit);
            UseCase = new ListSymptomCheckIns(
                authorizer,
                EpisodeRepository,
                Repository,
                Provider,
                Cursor,
                new SymptomDiaryPackageValidator(),
                new FakeAuditLogger());
        }

        public EligibleSymptomDiaryEpisode Episode { get; }
        public SymptomDiaryPackageContent Content { get; }
        public FakeEpisodeRepository EpisodeRepository { get; } = new();
        public FakeAuthorizationRepository AuthorizationRepository { get; } = new();
        public FakeReadRepository Repository { get; } = new();
        public FakeBatchProvider Provider { get; } = new();
        public SymptomDiaryHistoryCursorCodec Cursor { get; } =
            new("phase96-unit-signing-key-with-sufficient-entropy");
        public ListSymptomCheckIns UseCase { get; }

        public SymptomCheckInHistoryRecord Record(
            int id,
            string submittedJson = "[\"less-than-one-day\"]",
            int createdAtOffset = 0)
        {
            var question = Content.Definition.Questions[0];
            return new SymptomCheckInHistoryRecord(
                EntityId.From(Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}")),
                Episode.EpisodeId,
                Content.PackageVersionId,
                Time.AddMinutes(createdAtOffset),
                [new SymptomCheckInHistoryAnswerRecord(
                    question.Code,
                    question.SourceOrder,
                    submittedJson)]);
        }

        public Task<ListSymptomCheckInsResult> ExecuteAsync(
            string? cursor = null,
            int? pageSize = null) =>
            UseCase.ExecuteAsync(new ListSymptomCheckInsQuery(
                Episode.EpisodeId,
                cursor,
                pageSize));
    }

    private sealed class FakeReadRepository : ISymptomCheckInReadRepository
    {
        public IReadOnlyList<SymptomCheckInHistoryRecord> Results { get; set; } = [];
        public bool CursorExists { get; set; } = true;
        public int LastTake { get; private set; }
        public int ListCalls { get; private set; }

        public Task<bool> CursorExistsAsync(SymptomCheckInPageCursor cursor,
            CancellationToken cancellationToken = default) => Task.FromResult(CursorExists &&
                Results.Any(item => item.CheckInId == cursor.CheckInId &&
                    item.EpisodeId == cursor.EpisodeId && item.CreatedAt == cursor.CreatedAt));

        public Task<IReadOnlyList<SymptomCheckInHistoryRecord>> ListAsync(
            EntityId episodeId, SymptomCheckInPageCursor? after, int take,
            CancellationToken cancellationToken = default)
        {
            ListCalls++;
            LastTake = take;
            var values = Results.Where(item => item.EpisodeId == episodeId);
            if (after is not null)
            {
                var boundary = Results.ToList().FindIndex(item =>
                    item.CheckInId == after.CheckInId && item.CreatedAt == after.CreatedAt);
                values = values.Skip(boundary + 1);
            }

            return Task.FromResult<IReadOnlyList<SymptomCheckInHistoryRecord>>(
                values.Take(take).ToArray());
        }
    }

    private sealed class FakeBatchProvider : ISymptomDiaryExactContentBatchProvider
    {
        public Dictionary<EntityId, SymptomDiaryPackageContent> Packages { get; } = [];
        public IReadOnlyCollection<EntityId> RequestedIds { get; private set; } = [];

        public Task<IReadOnlyDictionary<EntityId, SymptomDiaryPackageContent>>
            GetExactPackagesAsync(IReadOnlyCollection<EntityId> packageVersionIds,
                CancellationToken cancellationToken = default)
        {
            RequestedIds = packageVersionIds;
            return Task.FromResult<IReadOnlyDictionary<EntityId, SymptomDiaryPackageContent>>(
                Packages.Where(item => packageVersionIds.Contains(item.Key))
                    .ToDictionary());
        }
    }

    private sealed class FakeEpisodeRepository : ISymptomDiaryEpisodeReadRepository
    {
        public EligibleSymptomDiaryEpisode? Result { get; set; }

        public Task<EligibleSymptomDiaryEpisode?> GetEligibleAsync(EntityId episodeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result?.EpisodeId == episodeId ? Result : null);
    }

    private sealed class FakeAuthorizationRepository : IPatientAccessAuthorizationRepository
    {
        public Task<PatientAccessAuthorizationLookup> FindAsync(EntityId managerProfileId,
            EntityId targetProfileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PatientAccessAuthorizationLookup(false, null));
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow =>
            new(2026, 9, 7, 18, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeAuditLogger : ISymptomDiaryHistoryAuditLogger
    {
        public void HistoricalContentUnavailable(
            EntityId episodeId,
            int packageVersionCount)
        {
        }
    }
}
