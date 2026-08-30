using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class Phase77DirectorySecurityAcceptanceTests(PostgreSqlContainerFixture postgres)
{
    private static readonly Guid AuroraClinicId = Guid.Parse(
        "71020000-0000-4000-8000-000000000001");
    private static readonly Guid AmberDoctorId = Guid.Parse(
        "71020000-0000-4200-8000-000000000021");

    [Fact]
    public async Task InvalidBearer_DoesNotChangeAnonymousSurfaceOrExposeInternalData()
    {
        using var context = await CreateContextAsync();
        context.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "invalid-expired-or-malformed-token");

        var endpoints = new[]
        {
            "/api/v1/clinics",
            $"/api/v1/clinics/{AuroraClinicId:D}",
            "/api/v1/doctors",
            $"/api/v1/doctors/{AmberDoctorId:D}"
        };
        foreach (var endpoint in endpoints)
        {
            using var response = await context.Client.GetAsync(endpoint);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertPublicBodyContainsNoInternalData(body);
        }
    }

    [Fact]
    public async Task MalformedInputs_ReturnSafeStatusWithoutCursorOrPersistenceDisclosure()
    {
        using var context = await CreateContextAsync();
        var overlong = Uri.EscapeDataString(new string('x', 101));
        var unknownClinicCursor = EncodeJson(new
        {
            v = 99,
            clinicId = AuroraClinicId,
            code = (string?)null,
            locality = (string?)null,
            administrativeArea = (string?)null,
            country = (string?)null
        });
        var unknownDoctorCursor = EncodeJson(new
        {
            v = 99,
            doctorId = AmberDoctorId,
            specialtyCode = (string?)null,
            languageCode = (string?)null,
            locality = (string?)null,
            administrativeArea = (string?)null,
            country = (string?)null,
            insurancePlanCode = (string?)null
        });
        var cases = new[]
        {
            new InvalidCase(
                "/api/v1/clinics?cursor=e30",
                "clinic_directory.cursor_invalid"),
            new InvalidCase(
                $"/api/v1/clinics?cursor={unknownClinicCursor}",
                "clinic_directory.cursor_invalid"),
            new InvalidCase(
                $"/api/v1/clinics?country={overlong}",
                "clinic_directory.filter_invalid"),
            new InvalidCase(
                "/api/v1/clinics?code=%20",
                "clinic_directory.filter_invalid"),
            new InvalidCase(
                "/api/v1/clinics?pageSize=-2147483648",
                "clinic_directory.page_size_invalid"),
            new InvalidCase(
                "/api/v1/doctors?cursor=e30",
                "doctor_directory.cursor_invalid"),
            new InvalidCase(
                $"/api/v1/doctors?cursor={unknownDoctorCursor}",
                "doctor_directory.cursor_invalid"),
            new InvalidCase(
                $"/api/v1/doctors?locality={overlong}",
                "doctor_directory.filter_invalid"),
            new InvalidCase(
                "/api/v1/doctors?languageCode=%20",
                "doctor_directory.filter_invalid"),
            new InvalidCase(
                "/api/v1/doctors?pageSize=2147483647",
                "doctor_directory.page_size_invalid")
        };

        foreach (var invalidCase in cases)
        {
            using var response = await context.Client.GetAsync(invalidCase.Endpoint);
            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal(
                invalidCase.ErrorCode,
                document.RootElement.GetProperty("errorCode").GetString());
            Assert.Equal("Request validation failed.",
                document.RootElement.GetProperty("title").GetString());
            AssertPublicBodyContainsNoInternalData(body);
        }

        using var malformedClinicId = await context.Client.GetAsync(
            "/api/v1/clinics/not-a-uuid");
        using var malformedDoctorId = await context.Client.GetAsync(
            "/api/v1/doctors/not-a-uuid");
        using var emptyClinicId = await context.Client.GetAsync(
            "/api/v1/clinics/00000000-0000-0000-0000-000000000000");
        using var emptyDoctorId = await context.Client.GetAsync(
            "/api/v1/doctors/00000000-0000-0000-0000-000000000000");
        Assert.Equal(HttpStatusCode.NotFound, malformedClinicId.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformedDoctorId.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, emptyClinicId.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, emptyDoctorId.StatusCode);
        AssertPublicBodyContainsNoInternalData(
            await malformedClinicId.Content.ReadAsStringAsync());
        AssertPublicBodyContainsNoInternalData(
            await malformedDoctorId.Content.ReadAsStringAsync());
        AssertPublicBodyContainsNoInternalData(
            await emptyClinicId.Content.ReadAsStringAsync());
        AssertPublicBodyContainsNoInternalData(
            await emptyDoctorId.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CasingAndUnicodeRemainExactValidUnknownValues()
    {
        using var context = await CreateContextAsync();

        using var clinicResponse = await context.Client.GetAsync(
            "/api/v1/clinics?code=DEMO-CLINIC-AURORA");
        var clinicPage = await clinicResponse.Content.ReadFromJsonAsync<DirectoryPage>();
        using var doctorResponse = await context.Client.GetAsync(
            "/api/v1/doctors?specialtyCode=d%C3%A9m%C3%B8-specialty");
        var doctorPage = await doctorResponse.Content.ReadFromJsonAsync<DirectoryPage>();

        Assert.Equal(HttpStatusCode.OK, clinicResponse.StatusCode);
        Assert.Empty(clinicPage!.Items);
        Assert.Null(clinicPage.NextCursor);
        Assert.Equal(HttpStatusCode.OK, doctorResponse.StatusCode);
        Assert.Empty(doctorPage!.Items);
        Assert.Null(doctorPage.NextCursor);
    }

    private static void AssertPublicBodyContainsNoInternalData(string body)
    {
        foreach (var forbidden in new[]
        {
            "Npgsql",
            "PostgreSQL",
            "connectionString",
            "stackTrace",
            "contentHash",
            "packageCode",
            "importedAt",
            "isPublished",
            "credentialStatus",
            "pending_verification",
            "submitted",
            "rejected",
            "evidence",
            "documentUrl",
            "Claim Amber"
        })
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string EncodeJson<T>(T value) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private async Task<TestContext> CreateContextAsync()
    {
        await using (var dbContext = new BeeexyDbContext(
            new DbContextOptionsBuilder<BeeexyDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options))
        {
            await dbContext.Database.MigrateAsync();
        }

        var factory = new BeeexyApiFactory(postgres.ConnectionString);
        return new TestContext(factory, factory.CreateApiClient());
    }

    private sealed record InvalidCase(string Endpoint, string ErrorCode);

    private sealed record DirectoryPage(
        IReadOnlyList<JsonElement> Items,
        string? NextCursor);

    private sealed record TestContext(BeeexyApiFactory Factory, HttpClient Client) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }
}
