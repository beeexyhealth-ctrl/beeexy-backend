using Beeexy.Application.Common;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;

namespace Beeexy.Tests.Unit.ClinicDirectory;

public sealed class ClinicDirectoryUseCaseTests
{
    [Fact]
    public async Task List_UsesLookaheadAndOpaqueCursorToResumeWithoutDuplicates()
    {
        var items = Enumerable.Range(1, 3)
            .Select(value => Item(value))
            .ToArray();
        var repository = new StubRepository(items);
        var useCase = new ListClinics(repository);

        var first = await useCase.ExecuteAsync(new ListClinicsQuery(PageSize: 2));

        Assert.Equal(items[..2], first.Items);
        Assert.NotNull(first.NextCursor);
        Assert.DoesNotContain(items[1].ClinicId.Value.ToString(), first.NextCursor!);
        Assert.Equal(3, repository.LastTake);

        var second = await useCase.ExecuteAsync(new ListClinicsQuery(
            Cursor: first.NextCursor,
            PageSize: 2));

        Assert.Equal([items[2]], second.Items);
        Assert.Null(second.NextCursor);
        Assert.Equal(items[1].ClinicId, repository.LastAfter!.ClinicId);
    }

    [Fact]
    public async Task List_NormalizesExactStoredValueFiltersBeforeRepositoryQuery()
    {
        var repository = new StubRepository([]);

        await new ListClinics(repository).ExecuteAsync(new ListClinicsQuery(
            Code: " demo-clinic ",
            Locality: " Demo Central ",
            AdministrativeArea: " Demo Region ",
            Country: " Demo Country "));

        Assert.Equal(
            new ClinicDirectoryFilter(
                "demo-clinic",
                "Demo Central",
                "Demo Region",
                "Demo Country"),
            repository.LastFilter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task List_RejectsInvalidPageSize(int pageSize)
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            new ListClinics(new StubRepository([])).ExecuteAsync(
                new ListClinicsQuery(PageSize: pageSize)));

        Assert.Equal("clinic_directory.page_size_invalid", exception.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task List_RejectsInvalidExactFilter(string country)
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            new ListClinics(new StubRepository([])).ExecuteAsync(
                new ListClinicsQuery(Country: country)));

        Assert.Equal("clinic_directory.filter_invalid", exception.Code);
    }

    [Fact]
    public async Task List_RejectsMalformedFilterMismatchedAndMissingBoundaryCursors()
    {
        var repository = new StubRepository([Item(1), Item(2)]);
        var useCase = new ListClinics(repository);
        var malformed = await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(new ListClinicsQuery(Cursor: "not+a+cursor")));
        Assert.Equal("clinic_directory.cursor_invalid", malformed.Code);

        var first = await useCase.ExecuteAsync(new ListClinicsQuery(
            PageSize: 1,
            Country: "Synthetic Country"));
        var tamperedCursor = first.NextCursor![..^1] +
            (first.NextCursor[^1] == 'A' ? 'B' : 'A');
        var tampered = await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(new ListClinicsQuery(
                Cursor: tamperedCursor,
                PageSize: 1,
                Country: "Synthetic Country")));
        Assert.Equal("clinic_directory.cursor_invalid", tampered.Code);

        var mismatch = await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(new ListClinicsQuery(
                Cursor: first.NextCursor,
                PageSize: 1)));
        Assert.Equal("clinic_directory.cursor_invalid", mismatch.Code);

        repository.CursorExists = false;
        var missing = await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(new ListClinicsQuery(
                Cursor: first.NextCursor,
                PageSize: 1,
                Country: "Synthetic Country")));
        Assert.Equal("clinic_directory.cursor_invalid", missing.Code);
    }

    [Fact]
    public async Task Get_ReturnsRepositoryProjectionAndConcealsMissingClinic()
    {
        var item = Item(1);
        var detail = new ClinicDirectoryDetail(
            item.ClinicId,
            item.Code,
            item.Name,
            [new ClinicDirectoryLocation(
                Id(11),
                "Synthetic Location",
                "Demo Locality",
                "Demo Region",
                "Demo Country",
                "America/Lima")]);
        var repository = new StubRepository([], detail);
        var useCase = new GetClinic(repository);

        Assert.Equal(detail, await useCase.ExecuteAsync(item.ClinicId));

        repository.Detail = null;
        await Assert.ThrowsAsync<ClinicNotFoundException>(() =>
            useCase.ExecuteAsync(item.ClinicId));
    }

    private static ClinicDirectoryListItem Item(int suffix) =>
        new(Id(suffix), $"clinic-{suffix}", $"Synthetic Clinic {suffix}");

    private static EntityId Id(int suffix) => EntityId.From(Guid.Parse(
        $"71030000-0000-4000-8000-{suffix:D12}"));

    private sealed class StubRepository(
        IReadOnlyList<ClinicDirectoryListItem> items,
        ClinicDirectoryDetail? detail = null) : IClinicDirectoryReadRepository
    {
        public bool CursorExists { get; set; } = true;

        public ClinicDirectoryDetail? Detail { get; set; } = detail;

        public ClinicDirectoryFilter? LastFilter { get; private set; }

        public ClinicDirectoryPageCursor? LastAfter { get; private set; }

        public int LastTake { get; private set; }

        public Task<bool> CursorExistsAsync(
            ClinicDirectoryPageCursor cursor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CursorExists);

        public Task<IReadOnlyList<ClinicDirectoryListItem>> ListAsync(
            ClinicDirectoryFilter filter,
            ClinicDirectoryPageCursor? after,
            int take,
            CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            LastAfter = after;
            LastTake = take;
            IReadOnlyList<ClinicDirectoryListItem> page = items
                .Where(item => after is null ||
                    item.ClinicId.Value.CompareTo(after.ClinicId.Value) > 0)
                .OrderBy(item => item.ClinicId.Value)
                .Take(take)
                .ToArray();
            return Task.FromResult(page);
        }

        public Task<ClinicDirectoryDetail?> GetAsync(
            EntityId clinicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Detail);
    }
}
