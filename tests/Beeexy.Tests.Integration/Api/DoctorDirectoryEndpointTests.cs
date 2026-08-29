using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class DoctorDirectoryEndpointTests(PostgreSqlContainerFixture postgres)
{
    private static readonly Guid AmberId =
        Guid.Parse("71020000-0000-4200-8000-000000000021");
    private static readonly Guid BlueId =
        Guid.Parse("71020000-0000-4200-8000-000000000022");
    private static readonly Guid CoralId =
        Guid.Parse("71020000-0000-4200-8000-000000000023");
    private static readonly Guid DuskId =
        Guid.Parse("71020000-0000-4200-8000-000000000024");
    private static readonly Guid EmberId =
        Guid.Parse("71020000-0000-4200-8000-000000000025");

    [Fact]
    public async Task AnonymousSearch_UsesNeutralStableCursorAndProjectsOnlyEligibleRelationships()
    {
        using var context = await CreateContextAsync();

        using var firstResponse = await context.Client.GetAsync("/api/v1/doctors?pageSize=2");
        var first = await firstResponse.Content.ReadFromJsonAsync<DoctorPage>();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(first);
        Assert.Equal([AmberId, BlueId], first.Items.Select(value => value.DoctorId).ToArray());
        Assert.NotNull(first.NextCursor);
        Assert.DoesNotContain(BlueId.ToString(), first.NextCursor!);

        var amber = first.Items[0];
        Assert.Equal("demo-doctor-amber", amber.Code);
        Assert.Equal("Synthetic Demo Doctor Amber", amber.DisplayName);
        Assert.Equal(["demo-specialty-general"], amber.Specialties.Select(value => value.Code));
        Assert.Equal(
            ["demo-language-en", "demo-language-es"],
            amber.Languages.Select(value => value.Code));
        Assert.Equal(
            ["demo-plan-amber", "demo-plan-blue"],
            amber.StoredInsuranceParticipations.Select(value => value.Code));
        Assert.Equal(
            "Synthetic Demo Dataset Credential Amber",
            Assert.Single(amber.Credentials).Name);
        var amberAffiliation = Assert.Single(amber.Affiliations);
        Assert.Equal("demo-clinic-aurora", amberAffiliation.ClinicCode);
        Assert.Equal("Demo Central", amberAffiliation.Location!.Locality);

        var blue = first.Items[1];
        Assert.Empty(blue.Credentials);
        var blueAffiliation = Assert.Single(blue.Affiliations);
        Assert.Equal("demo-clinic-mosaic", blueAffiliation.ClinicCode);
        Assert.Equal("Demo Harbor", blueAffiliation.Location!.Locality);
        Assert.DoesNotContain(blue.Affiliations, value =>
            value.Location?.Name.Contains("Hidden", StringComparison.Ordinal) == true);

        using var secondResponse = await context.Client.GetAsync(
            "/api/v1/doctors?pageSize=2&cursor=" +
            Uri.EscapeDataString(first.NextCursor!));
        var second = await secondResponse.Content.ReadFromJsonAsync<DoctorPage>();

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(second);
        Assert.Equal([CoralId, EmberId], second.Items.Select(value => value.DoctorId).ToArray());
        Assert.Null(second.NextCursor);
        Assert.DoesNotContain(second.Items, value => value.DoctorId == DuskId);
        Assert.Empty(second.Items[0].Affiliations);
        var emberAffiliation = Assert.Single(second.Items[1].Affiliations);
        Assert.Equal("demo-clinic-mosaic", emberAffiliation.ClinicCode);
        Assert.Null(emberAffiliation.Location);
    }

    [Theory]
    [InlineData("specialtyCode=demo-specialty-general", "demo-doctor-amber,demo-doctor-blue")]
    [InlineData("languageCode=demo-language-pt", "demo-doctor-coral")]
    [InlineData("locality=Demo%20Central", "demo-doctor-amber")]
    [InlineData("country=Synthetic%20Demo%20Country", "demo-doctor-amber,demo-doctor-blue")]
    [InlineData("insurancePlanCode=demo-plan-blue", "demo-doctor-amber,demo-doctor-blue")]
    [InlineData(
        "specialtyCode=demo-specialty-child&languageCode=demo-language-es&" +
        "insurancePlanCode=demo-plan-coral",
        "demo-doctor-ember")]
    [InlineData(
        "specialtyCode=demo-specialty-general&languageCode=demo-language-es&" +
        "locality=Demo%20Harbor&administrativeArea=Synthetic%20Demo%20Region&" +
        "country=Synthetic%20Demo%20Country&insurancePlanCode=demo-plan-blue",
        "demo-doctor-blue")]
    public async Task Search_UsesExactStoredFiltersWithIntersectionSemantics(
        string query,
        string expectedCodes)
    {
        using var context = await CreateContextAsync();

        using var response = await context.Client.GetAsync($"/api/v1/doctors?{query}");
        var page = await response.Content.ReadFromJsonAsync<DoctorPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(expectedCodes.Split(','), page.Items.Select(value => value.Code));
    }

    [Fact]
    public async Task Search_ValidUnknownCanonicalValueReturnsEmptyPage()
    {
        using var context = await CreateContextAsync();

        using var response = await context.Client.GetAsync(
            "/api/v1/doctors?specialtyCode=demo-specialty-not-present");
        var page = await response.Content.ReadFromJsonAsync<DoctorPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Theory]
    [InlineData("/api/v1/doctors?cursor=not+a+cursor", "doctor_directory.cursor_invalid")]
    [InlineData("/api/v1/doctors?pageSize=0", "doctor_directory.page_size_invalid")]
    [InlineData("/api/v1/doctors?pageSize=101", "doctor_directory.page_size_invalid")]
    [InlineData("/api/v1/doctors?locality=%20%20", "doctor_directory.filter_invalid")]
    [InlineData("/api/v1/doctors?specialtyCode=not%20a%20code", "doctor_directory.filter_invalid")]
    [InlineData("/api/v1/doctors?rating=5", "doctor_directory.filter_unsupported")]
    [InlineData("/api/v1/doctors?languageCode=a&languageCode=b", "doctor_directory.filter_invalid")]
    public async Task Search_InvalidCursorPagingAndFiltersReturnSafeValidation(
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
    public async Task Detail_ConcealsHiddenDoctorAndExposesNoHiddenCredentialOrAffiliationState()
    {
        using var context = await CreateContextAsync();

        using var amberResponse = await context.Client.GetAsync($"/api/v1/doctors/{AmberId:D}");
        using var amberDocument = JsonDocument.Parse(
            await amberResponse.Content.ReadAsStringAsync());
        var amber = await amberResponse.Content.ReadFromJsonAsync<DoctorProfile>();

        Assert.Equal(HttpStatusCode.OK, amberResponse.StatusCode);
        Assert.NotNull(amber);
        Assert.Equal(AmberId, amber.DoctorId);
        Assert.Single(amber.Credentials);
        Assert.DoesNotContain("Claim Amber", amberDocument.RootElement.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(
            [
                "affiliations",
                "code",
                "credentials",
                "displayName",
                "doctorId",
                "languages",
                "specialties",
                "storedInsuranceParticipations"
            ],
            amberDocument.RootElement.EnumerateObject()
                .Select(value => value.Name)
                .Order()
                .ToArray());
        Assert.Equal(["name"], amberDocument.RootElement.GetProperty("credentials")[0]
            .EnumerateObject().Select(value => value.Name).ToArray());

        using var unpublishedResponse = await context.Client.GetAsync(
            $"/api/v1/doctors/{DuskId:D}");
        using var missingResponse = await context.Client.GetAsync(
            $"/api/v1/doctors/{Guid.NewGuid():D}");
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
    public async Task OpenApi_DocumentsOnlyTruthfulAnonymousDeterministicDoctorDirectory()
    {
        using var context = await CreateContextAsync();

        using var response = await context.Client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var search = paths.GetProperty("/api/v1/doctors").GetProperty("get");
        var detail = paths.GetProperty("/api/v1/doctors/{id}").GetProperty("get");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(36, paths.EnumerateObject().Count());
        Assert.Equal(2, paths.EnumerateObject().Count(path =>
            path.Name.StartsWith("/api/v1/doctors", StringComparison.Ordinal)));
        Assert.False(search.TryGetProperty("security", out _));
        Assert.False(detail.TryGetProperty("security", out _));
        Assert.Equal(
            [
                "cursor",
                "pageSize",
                "specialtyCode",
                "languageCode",
                "locality",
                "administrativeArea",
                "country",
                "insurancePlanCode"
            ],
            search.GetProperty("parameters").EnumerateArray()
                .Select(parameter => parameter.GetProperty("name").GetString())
                .ToArray());
        AssertResponseCodes(search, "200", "422", "500");
        AssertResponseCodes(detail, "200", "404", "500");
        var descriptions = search.GetProperty("description").GetString() + " " +
            detail.GetProperty("description").GetString();
        Assert.Contains("synthetic demo", descriptions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("neutral UUID order", descriptions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not current coverage", descriptions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not authoritative", descriptions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verified only within", descriptions, StringComparison.OrdinalIgnoreCase);

        var schemas = document.RootElement.GetProperty("components")
            .GetProperty("schemas")
            .EnumerateObject()
            .Where(schema => schema.Name.StartsWith("DoctorDirectory", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(schemas);
        var schemaText = string.Join(' ', schemas.Select(schema => schema.Value.ToString()));
        foreach (var forbidden in new[]
        {
            "isPublished", "credentialStatus", "submitted", "pendingVerification", "rejected",
            "rating", "review", "score", "recommend", "factor", "weight", "distance",
            "latitude", "longitude", "availability", "realTime", "import", "hash", "ledger",
            "Practitioner", "Organization"
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

    private sealed record DoctorPage(
        IReadOnlyList<DoctorProfile> Items,
        string? NextCursor);

    private sealed record DoctorProfile(
        Guid DoctorId,
        string Code,
        string DisplayName,
        IReadOnlyList<CatalogValue> Specialties,
        IReadOnlyList<CatalogValue> Languages,
        IReadOnlyList<Affiliation> Affiliations,
        IReadOnlyList<CatalogValue> StoredInsuranceParticipations,
        IReadOnlyList<Credential> Credentials);

    private sealed record CatalogValue(string Code, string Name);

    private sealed record Affiliation(
        Guid ClinicId,
        string ClinicCode,
        string ClinicName,
        Location? Location);

    private sealed record Location(
        Guid LocationId,
        string Name,
        string Locality,
        string AdministrativeArea,
        string Country,
        string TimeZone);

    private sealed record Credential(string Name);

    private sealed record TestContext(BeeexyApiFactory Factory, HttpClient Client) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }
}
