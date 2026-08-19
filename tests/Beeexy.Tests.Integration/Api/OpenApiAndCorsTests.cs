using System.Net;
using System.Text.Json;
using Beeexy.Tests.Integration.Support;
using Microsoft.Extensions.Hosting;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class OpenApiAndCorsTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task OpenApi_InDevelopment_IncludesHealthAndEmailChallengeEndpoints()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString());
        Assert.Equal(3, paths.EnumerateObject().Count());
        Assert.True(paths.GetProperty("/health/live").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/health/ready").TryGetProperty("get", out _));
        Assert.True(paths
            .GetProperty("/api/v1/auth/email/challenges")
            .TryGetProperty("post", out _));
    }

    [Fact]
    public async Task OpenApi_InProduction_IsNotExposed()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            Environments.Production);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProductionHttpsResponse_IncludesHsts()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            Environments.Production);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task ConfiguredCorsOrigin_IsAllowed()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", BeeexyApiFactory.AllowedCorsOrigin);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            BeeexyApiFactory.AllowedCorsOrigin,
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task UntrustedCorsOrigin_IsNotAllowed()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", "https://untrusted.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void WildcardCorsOrigin_IsRejectedAtStartup()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            allowedCorsOrigin: "*");

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());

        Assert.Contains("without wildcards", exception.ToString());
        Assert.DoesNotContain(postgres.ConnectionString, exception.ToString());
    }
}
