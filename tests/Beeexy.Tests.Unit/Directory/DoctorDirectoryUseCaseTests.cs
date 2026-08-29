using Beeexy.Application.Common;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;

namespace Beeexy.Tests.Unit.DoctorDirectory;

public sealed class DoctorDirectoryUseCaseTests
{
    [Fact]
    public async Task Search_UsesLookaheadAndOpaqueCursorToResumeWithoutDuplicates()
    {
        var items = Enumerable.Range(1, 3).Select(Profile).ToArray();
        var repository = new StubRepository(items);
        var useCase = UseCase(repository);

        var first = await useCase.ExecuteAsync(new SearchDoctorsQuery(PageSize: 2));

        Assert.Equal(items[..2], first.Items.Select(item => item.Profile));
        Assert.NotNull(first.NextCursor);
        Assert.DoesNotContain(items[1].DoctorId.Value.ToString(), first.NextCursor!);
        Assert.Equal(3, repository.LastTake);

        var second = await useCase.ExecuteAsync(new SearchDoctorsQuery(
            Cursor: first.NextCursor,
            PageSize: 2));

        Assert.Equal([items[2]], second.Items.Select(item => item.Profile));
        Assert.Null(second.NextCursor);
        Assert.Equal(items[1].DoctorId, repository.LastAfter!.DoctorId);
    }

    [Fact]
    public async Task Search_NormalizesCanonicalCodesAndExactLocationParts()
    {
        var repository = new StubRepository([]);

        await UseCase(repository).ExecuteAsync(new SearchDoctorsQuery(
            SpecialtyCode: " demo-specialty-general ",
            LanguageCode: " demo-language-es ",
            Locality: " Demo Central ",
            AdministrativeArea: " Demo Region ",
            Country: " Demo Country ",
            InsurancePlanCode: " demo-plan-blue "));

        Assert.Equal(
            new DoctorDirectoryFilter(
                "demo-specialty-general",
                "demo-language-es",
                "Demo Central",
                "Demo Region",
                "Demo Country",
                "demo-plan-blue"),
            repository.LastFilter);
    }

    [Fact]
    public async Task Search_NoCriteriaRetainsNeutralOrderAndOmitsMatchResults()
    {
        var repository = new StubRepository([Profile(2), Profile(1)]);

        var result = await UseCase(repository).ExecuteAsync(new SearchDoctorsQuery());

        Assert.Equal([Id(1), Id(2)], result.Items.Select(item => item.Profile.DoctorId));
        Assert.All(result.Items, item => Assert.Null(item.Match));
        Assert.Equal(0, repository.RuleReads);
    }

    [Fact]
    public async Task Search_CriteriaGloballyRanksThenPaginatesWithExactEngineResults()
    {
        var notMatched = Profile(1) with
        {
            Specialties = [new("specialty-b", "Synthetic Specialty B")]
        };
        var laterTie = Profile(3) with
        {
            Specialties = [new("specialty-a", "Synthetic Specialty A")]
        };
        var earlierTie = Profile(2) with
        {
            Specialties = [new("specialty-a", "Synthetic Specialty A")]
        };
        var repository = new StubRepository([notMatched, laterTie, earlierTie]);
        var useCase = UseCase(repository);
        var query = new SearchDoctorsQuery(PageSize: 1, SpecialtyCode: "specialty-a");

        var first = await useCase.ExecuteAsync(query);
        var second = await useCase.ExecuteAsync(query with { Cursor = first.NextCursor });
        var third = await useCase.ExecuteAsync(query with { Cursor = second.NextCursor });

        Assert.Equal(Id(2), Assert.Single(first.Items).Profile.DoctorId);
        Assert.Equal(Id(3), Assert.Single(second.Items).Profile.DoctorId);
        Assert.Equal(Id(1), Assert.Single(third.Items).Profile.DoctorId);
        Assert.NotNull(first.NextCursor);
        Assert.NotNull(second.NextCursor);
        Assert.Null(third.NextCursor);
        Assert.Equal([25, 25, 0], new[] { first, second, third }
            .Select(page => Assert.Single(page.Items).Match!.MatchScore));
        Assert.All(new[] { first, second, third }, page =>
        {
            var match = Assert.Single(page.Items).Match!;
            Assert.Equal(ProductApprovedDoctorMatchRule.Version, match.RuleVersion);
            Assert.Equal(DoctorMatchFactorCodes.Ordered, match.Factors.Select(
                factor => factor.FactorCode));
        });
        Assert.Equal(
            [Id(2), Id(3), Id(1)],
            new[] { first, second, third }
                .Select(page => Assert.Single(page.Items).Profile.DoctorId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Search_RejectsInvalidPageSize(int pageSize)
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            UseCase(new StubRepository([])).ExecuteAsync(
                new SearchDoctorsQuery(PageSize: pageSize)));

        Assert.Equal("doctor_directory.page_size_invalid", exception.Code);
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("value with spaces", null, null)]
    [InlineData(null, "   ", null)]
    [InlineData(null, null, "   ")]
    public async Task Search_RejectsMalformedCanonicalAndLocationFilters(
        string? specialtyCode,
        string? locality,
        string? insurancePlanCode)
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            UseCase(new StubRepository([])).ExecuteAsync(new SearchDoctorsQuery(
                SpecialtyCode: specialtyCode,
                Locality: locality,
                InsurancePlanCode: insurancePlanCode)));

        Assert.Equal("doctor_directory.filter_invalid", exception.Code);
    }

    [Fact]
    public async Task Search_RejectsMalformedTamperedFilterMismatchedAndMissingCursors()
    {
        var repository = new StubRepository([Profile(1), Profile(2)]);
        var useCase = UseCase(repository);
        var malformed = await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(new SearchDoctorsQuery(Cursor: "not+a+cursor")));
        Assert.Equal("doctor_directory.cursor_invalid", malformed.Code);

        var first = await useCase.ExecuteAsync(new SearchDoctorsQuery(
            PageSize: 1,
            SpecialtyCode: "demo-specialty-general"));
        var tamperedCursor = first.NextCursor![..^1] +
            (first.NextCursor[^1] == 'A' ? 'B' : 'A');
        var tampered = await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(new SearchDoctorsQuery(
                Cursor: tamperedCursor,
                PageSize: 1,
                SpecialtyCode: "demo-specialty-general")));
        Assert.Equal("doctor_directory.cursor_invalid", tampered.Code);

        var mismatch = await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(new SearchDoctorsQuery(
                Cursor: first.NextCursor,
                PageSize: 1,
                SpecialtyCode: "demo-specialty-child")));
        Assert.Equal("doctor_directory.cursor_invalid", mismatch.Code);

        repository.CursorExists = false;
        var missing = await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(new SearchDoctorsQuery(
                Cursor: first.NextCursor,
                PageSize: 1,
                SpecialtyCode: "demo-specialty-general")));
        Assert.Equal("doctor_directory.cursor_invalid", missing.Code);
    }

    [Fact]
    public async Task Get_ReturnsPublicProjectionAndConcealsMissingDoctor()
    {
        var detail = Profile(1);
        var repository = new StubRepository([], detail);
        var useCase = new GetDoctor(repository);

        Assert.Equal(detail, await useCase.ExecuteAsync(detail.DoctorId));

        repository.Detail = null;
        await Assert.ThrowsAsync<DoctorNotFoundException>(() =>
            useCase.ExecuteAsync(detail.DoctorId));
    }

    private static DoctorDirectoryProfile Profile(int suffix) =>
        new(
            Id(suffix),
            $"doctor-{suffix}",
            $"Synthetic Doctor {suffix}",
            [new DoctorDirectoryCatalogValue("specialty", "Synthetic Specialty")],
            [new DoctorDirectoryCatalogValue("language", "Synthetic Language")],
            [],
            [new DoctorDirectoryCatalogValue("plan", "Synthetic Stored Plan")],
            [new DoctorDirectoryCredential("Synthetic Dataset Credential")]);

    private static EntityId Id(int suffix) => EntityId.From(Guid.Parse(
        $"71040000-0000-4000-8000-{suffix:D12}"));

    private static SearchDoctors UseCase(StubRepository repository) => new(
        repository,
        new CalculateDoctorMatch(repository, new DeterministicDoctorMatchEngine()));

    private sealed class StubRepository(
        IReadOnlyList<DoctorDirectoryProfile> items,
        DoctorDirectoryProfile? detail = null) :
        IDoctorDirectoryReadRepository,
        IDoctorMatchingRepository
    {
        public bool CursorExists { get; set; } = true;

        public DoctorDirectoryProfile? Detail { get; set; } = detail;

        public DoctorDirectoryFilter? LastFilter { get; private set; }

        public DoctorDirectoryPageCursor? LastAfter { get; private set; }

        public int LastTake { get; private set; }

        public int RuleReads { get; private set; }

        public Task<bool> CursorExistsAsync(
            DoctorDirectoryPageCursor cursor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CursorExists);

        public Task<IReadOnlyList<DoctorDirectoryProfile>> SearchAsync(
            DoctorDirectoryFilter filter,
            DoctorDirectoryPageCursor? after,
            int take,
            CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            LastAfter = after;
            LastTake = take;
            IReadOnlyList<DoctorDirectoryProfile> page = items
                .Where(item => after is null ||
                    item.DoctorId.Value.CompareTo(after.DoctorId.Value) > 0)
                .OrderBy(item => item.DoctorId.Value)
                .Take(take)
                .ToArray();
            return Task.FromResult(page);
        }

        public Task<IReadOnlyList<EntityId>> ListFilteredDoctorIdsAsync(
            DoctorDirectoryFilter filter,
            CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            return Task.FromResult<IReadOnlyList<EntityId>>(
                CursorExists ? items.Select(item => item.DoctorId).ToArray() : []);
        }

        public Task<IReadOnlyList<DoctorDirectoryProfile>> GetManyAsync(
            IReadOnlyList<EntityId> doctorIds,
            CancellationToken cancellationToken = default)
        {
            var profiles = items.ToDictionary(item => item.DoctorId);
            return Task.FromResult<IReadOnlyList<DoctorDirectoryProfile>>(
                doctorIds.Select(doctorId => profiles[doctorId]).ToArray());
        }

        public Task<DoctorDirectoryProfile?> GetAsync(
            EntityId doctorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Detail);

        public Task<DoctorMatchRuleDefinition?> GetRuleAsync(
            DirectoryCode version,
            CancellationToken cancellationToken = default)
        {
            RuleReads++;
            return Task.FromResult<DoctorMatchRuleDefinition?>(new(
                "beeexy-demo-doctor-match-rules",
                ProductApprovedDoctorMatchRule.Version,
                new string('a', 64),
                25,
                25,
                25,
                25));
        }

        public Task<IReadOnlyList<DoctorMatchCandidateSnapshot>>
            ListEligibleCandidatesAsync(
                IReadOnlyCollection<EntityId>? doctorIds = null,
                CancellationToken cancellationToken = default)
        {
            var selected = items
                .Where(item => doctorIds is null || doctorIds.Contains(item.DoctorId))
                .Select(item => new DoctorMatchCandidateSnapshot(
                    item.DoctorId,
                    item.Specialties.Select(value => value.Code).ToArray(),
                    item.Languages.Select(value => value.Code).ToArray(),
                    item.Affiliations.Where(value => value.Location is not null).Select(value =>
                        new DoctorMatchCandidateLocation(
                            value.Location!.Locality,
                            value.Location.AdministrativeArea,
                            value.Location.Country)).ToArray(),
                    item.StoredInsuranceParticipations.Select(value => value.Code).ToArray()))
                .ToArray();
            return Task.FromResult<IReadOnlyList<DoctorMatchCandidateSnapshot>>(selected);
        }
    }
}
