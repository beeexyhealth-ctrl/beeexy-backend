using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class ClinicDirectoryEndpointTests(PostgreSqlContainerFixture postgres)
{
    private static readonly Guid AuroraId =
        Guid.Parse("71020000-0000-4000-8000-000000000001");
    private static readonly Guid ArchiveId =
        Guid.Parse("71020000-0000-4000-8000-000000000003");

    [Fact]
    public async Task AnonymousList_UsesExactFiltersAndStableOpaqueKeysetPagination()
    {
        using var context = await CreateContextAsync();

        using var firstResponse = await context.Client.GetAsync(
            "/api/v1/clinics?pageSize=1&country=Synthetic%20Demo%20Country");
        var first = await firstResponse.Content.ReadFromJsonAsync<ClinicPage>();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(first);
        var firstItem = Assert.Single(first.Items);
        Assert.Equal("demo-clinic-aurora", firstItem.Code);
        Assert.Equal("Synthetic Demo Clinic Aurora", firstItem.Name);
        Assert.NotEqual(Guid.Empty, firstItem.ClinicId);
        Assert.NotNull(first.NextCursor);
        Assert.DoesNotContain(firstItem.ClinicId.ToString(), first.NextCursor!);

        using var secondResponse = await context.Client.GetAsync(
            "/api/v1/clinics?pageSize=1&country=Synthetic%20Demo%20Country&cursor=" +
            Uri.EscapeDataString(first.NextCursor!));
        var second = await secondResponse.Content.ReadFromJsonAsync<ClinicPage>();

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(second);
        Assert.Equal("demo-clinic-mosaic", Assert.Single(second.Items).Code);
        Assert.Null(second.NextCursor);
        Assert.NotEqual(firstItem.ClinicId, second.Items[0].ClinicId);

        var tamperedCursor = first.NextCursor![..^1] +
            (first.NextCursor[^1] == 'A' ? 'B' : 'A');
        using var tamperedResponse = await context.Client.GetAsync(
            "/api/v1/clinics?pageSize=1&country=Synthetic%20Demo%20Country&cursor=" +
            Uri.EscapeDataString(tamperedCursor));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tamperedResponse.StatusCode);

        using var codeResponse = await context.Client.GetAsync(
            "/api/v1/clinics?code=demo-clinic-aurora&locality=Demo%20Central&" +
            "administrativeArea=Synthetic%20Demo%20Region&" +
            "country=Synthetic%20Demo%20Country");
        var codePage = await codeResponse.Content.ReadFromJsonAsync<ClinicPage>();
        Assert.Equal(HttpStatusCode.OK, codeResponse.StatusCode);
        Assert.Equal("demo-clinic-aurora", Assert.Single(codePage!.Items).Code);

        using var emptyResponse = await context.Client.GetAsync(
            "/api/v1/clinics?country=Country%20That%20Does%20Not%20Exist");
        var empty = await emptyResponse.Content.ReadFromJsonAsync<ClinicPage>();
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.Empty(empty!.Items);
        Assert.Null(empty.NextCursor);

        using var allResponse = await context.Client.GetAsync("/api/v1/clinics");
        var all = await allResponse.Content.ReadFromJsonAsync<ClinicPage>();
        Assert.Equal(2, all!.Items.Count);
        Assert.DoesNotContain(all.Items, item => item.Code == "demo-clinic-archive");
    }

    [Theory]
    [InlineData("/api/v1/clinics?cursor=not+a+cursor", "clinic_directory.cursor_invalid")]
    [InlineData("/api/v1/clinics?pageSize=0", "clinic_directory.page_size_invalid")]
    [InlineData("/api/v1/clinics?pageSize=101", "clinic_directory.page_size_invalid")]
    [InlineData("/api/v1/clinics?country=%20%20", "clinic_directory.filter_invalid")]
    [InlineData("/api/v1/clinics?rating=5", "clinic_directory.filter_unsupported")]
    [InlineData("/api/v1/clinics?country=A&country=B", "clinic_directory.filter_invalid")]
    public async Task List_InvalidCursorPagingAndFiltersReturnSafeValidation(
        string endpoint,
        string errorCode)
    {
        using var context = await CreateContextAsync();

        using var response = await context.Client.GetAsync(endpoint);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(errorCode, document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Detail_ReturnsOnlyEligibleStoredLocationsAndConcealsUnpublishedClinic()
    {
        using var context = await CreateContextAsync();

        using var response = await context.Client.GetAsync($"/api/v1/clinics/{AuroraId:D}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var detail = await response.Content.ReadFromJsonAsync<ClinicDetail>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(detail);
        Assert.Equal(AuroraId, detail.ClinicId);
        Assert.Equal("demo-clinic-aurora", detail.Code);
        var location = Assert.Single(detail.Locations);
        Assert.Equal("Synthetic Aurora Central Location", location.Name);
        Assert.Equal("Demo Central", location.Locality);
        Assert.Equal("Synthetic Demo Region", location.AdministrativeArea);
        Assert.Equal("Synthetic Demo Country", location.Country);
        Assert.Equal("America/Lima", location.TimeZone);
        Assert.DoesNotContain(detail.Locations, value => value.Name.Contains("Hidden"));
        Assert.Equal(
            ["clinicId", "code", "locations", "name"],
            document.RootElement.EnumerateObject().Select(value => value.Name).Order().ToArray());

        using var unpublishedResponse = await context.Client.GetAsync(
            $"/api/v1/clinics/{ArchiveId:D}");
        using var missingResponse = await context.Client.GetAsync(
            $"/api/v1/clinics/{Guid.NewGuid():D}");
        using var unpublished = JsonDocument.Parse(
            await unpublishedResponse.Content.ReadAsStringAsync());
        using var missing = JsonDocument.Parse(await missingResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, unpublishedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(
            unpublished.RootElement.GetProperty("title").GetString(),
            missing.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            unpublished.RootElement.GetProperty("detail").GetString(),
            missing.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task OpenApi_DocumentsOnlyTheTruthfulAnonymousClinicDirectorySurface()
    {
        using var context = await CreateContextAsync();

        using var response = await context.Client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var list = paths.GetProperty("/api/v1/clinics").GetProperty("get");
        var detail = paths.GetProperty("/api/v1/clinics/{id}").GetProperty("get");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(41, paths.EnumerateObject().Count());
        Assert.False(list.TryGetProperty("security", out _));
        Assert.False(detail.TryGetProperty("security", out _));
        Assert.Contains("synthetic demo", list.GetProperty("description").GetString()!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not authoritative", list.GetProperty("description").GetString()!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ["cursor", "pageSize", "code", "locality", "administrativeArea", "country"],
            list.GetProperty("parameters").EnumerateArray()
                .Select(parameter => parameter.GetProperty("name").GetString())
                .ToArray());
        AssertResponseCodes(list, "200", "422", "500");
        AssertResponseCodes(detail, "200", "404", "500");
        Assert.DoesNotContain(paths.EnumerateObject(), path =>
            path.Name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
            path.Name.Contains("match", StringComparison.OrdinalIgnoreCase));

        var clinicSchemas = document.RootElement.GetProperty("components")
            .GetProperty("schemas")
            .EnumerateObject()
            .Where(schema => schema.Name.StartsWith("ClinicDirectory", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(clinicSchemas);
        var schemaText = string.Join(' ', clinicSchemas.Select(schema => schema.Value.ToString()));
        foreach (var forbidden in new[]
        {
            "rating", "review", "verified", "latitude", "longitude", "distance",
            "openingHours", "availability", "insurance", "isPublished", "import"
        })
        {
            Assert.DoesNotContain(forbidden, schemaText, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<TestContext> CreateContextAsync()
    {
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Database.MigrateAsync();
        }

        var factory = new BeeexyApiFactory(postgres.ConnectionString);
        var client = factory.CreateApiClient();
        return new TestContext(factory, client);
    }

    private BeeexyDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private static void AssertResponseCodes(JsonElement operation, params string[] expected) =>
        Assert.Equal(
            expected.Order(),
            operation.GetProperty("responses").EnumerateObject()
                .Select(response => response.Name)
                .Order());

    private sealed record ClinicPage(
        IReadOnlyList<ClinicItem> Items,
        string? NextCursor);

    private sealed record ClinicItem(Guid ClinicId, string Code, string Name);

    private sealed record ClinicDetail(
        Guid ClinicId,
        string Code,
        string Name,
        IReadOnlyList<ClinicLocation> Locations);

    private sealed record ClinicLocation(
        Guid LocationId,
        string Name,
        string Locality,
        string AdministrativeArea,
        string Country,
        string TimeZone);

    private sealed record TestContext(BeeexyApiFactory Factory, HttpClient Client) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }
}
