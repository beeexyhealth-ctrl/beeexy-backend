using Beeexy.Application.Common;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;

namespace Beeexy.Tests.Unit.DoctorDirectory;

public sealed class DoctorDirectoryUseCaseTests
{
    [Fact]
    public async Task Search_UsesLookaheadAndOpaqueCursorToResumeWithoutDuplicates()
    {
        var items = Enumerable.Range(1, 3).Select(Profile).ToArray();
        var repository = new StubRepository(items);
        var useCase = new SearchDoctors(repository);

        var first = await useCase.ExecuteAsync(new SearchDoctorsQuery(PageSize: 2));

        Assert.Equal(items[..2], first.Items);
        Assert.NotNull(first.NextCursor);
        Assert.DoesNotContain(items[1].DoctorId.Value.ToString(), first.NextCursor!);
        Assert.Equal(3, repository.LastTake);

        var second = await useCase.ExecuteAsync(new SearchDoctorsQuery(
            Cursor: first.NextCursor,
            PageSize: 2));

        Assert.Equal([items[2]], second.Items);
        Assert.Null(second.NextCursor);
        Assert.Equal(items[1].DoctorId, repository.LastAfter!.DoctorId);
    }

    [Fact]
    public async Task Search_NormalizesCanonicalCodesAndExactLocationParts()
    {
        var repository = new StubRepository([]);

        await new SearchDoctors(repository).ExecuteAsync(new SearchDoctorsQuery(
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

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Search_RejectsInvalidPageSize(int pageSize)
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            new SearchDoctors(new StubRepository([])).ExecuteAsync(
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
            new SearchDoctors(new StubRepository([])).ExecuteAsync(new SearchDoctorsQuery(
                SpecialtyCode: specialtyCode,
                Locality: locality,
                InsurancePlanCode: insurancePlanCode)));

        Assert.Equal("doctor_directory.filter_invalid", exception.Code);
    }

    [Fact]
    public async Task Search_RejectsMalformedTamperedFilterMismatchedAndMissingCursors()
    {
        var repository = new StubRepository([Profile(1), Profile(2)]);
        var useCase = new SearchDoctors(repository);
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

    private sealed class StubRepository(
        IReadOnlyList<DoctorDirectoryProfile> items,
        DoctorDirectoryProfile? detail = null) : IDoctorDirectoryReadRepository
    {
        public bool CursorExists { get; set; } = true;

        public DoctorDirectoryProfile? Detail { get; set; } = detail;

        public DoctorDirectoryFilter? LastFilter { get; private set; }

        public DoctorDirectoryPageCursor? LastAfter { get; private set; }

        public int LastTake { get; private set; }

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

        public Task<DoctorDirectoryProfile?> GetAsync(
            EntityId doctorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Detail);
    }
}
