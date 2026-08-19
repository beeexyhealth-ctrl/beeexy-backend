using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Beeexy.Tests.Integration.Support;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class HealthEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string UnavailablePassword = "unavailable-secret-must-not-leak";
    private const string UnavailableConnectionString =
        "Host=127.0.0.1;Port=1;Database=unavailable_database;" +
        "Username=unavailable_user;Password=" + UnavailablePassword +
        ";Timeout=1;Command Timeout=1;Include Error Detail=false";

    [Fact]
    public async Task Live_WithAvailablePostgreSql_ReturnsPublicHealthyContract()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadFromJsonAsync<HealthResponseContract>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", body?.Status);
        Assert.False(string.IsNullOrWhiteSpace(body?.CorrelationId));
        Assert.False(response.Headers.Contains("WWW-Authenticate"));
    }

    [Fact]
    public async Task Ready_WithRealPostgreSql_ReturnsHealthy()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<HealthResponseContract>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body?.Status);
        Assert.False(string.IsNullOrWhiteSpace(body?.CorrelationId));
    }

    [Fact]
    public async Task Live_WhenPostgreSqlIsUnavailable_RemainsHealthy()
    {
        using var factory = new BeeexyApiFactory(UnavailableConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadFromJsonAsync<HealthResponseContract>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body?.Status);
    }

    [Fact]
    public async Task Ready_WhenPostgreSqlIsUnavailable_ReturnsSafeServiceUnavailable()
    {
        using var factory = new BeeexyApiFactory(UnavailableConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/health/ready");
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.True(document.RootElement.TryGetProperty("correlationId", out var correlationId));
        Assert.False(string.IsNullOrWhiteSpace(correlationId.GetString()));
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain(UnavailablePassword, content, StringComparison.Ordinal);
        Assert.DoesNotContain("unavailable_database", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unavailable_user", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", content, StringComparison.Ordinal);
        Assert.DoesNotContain("connection", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvidedCorrelationId_IsPropagatedToHeaderAndBody()
    {
        const string correlationId = "phase1-integration-correlation";
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-ID", correlationId);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<HealthResponseContract>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal(correlationId, body?.CorrelationId);
    }

    [Fact]
    public async Task MissingCorrelationId_IsGeneratedAndReturned()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadFromJsonAsync<HealthResponseContract>();
        var header = response.Headers.GetValues("X-Correlation-ID").Single();

        Assert.Equal(32, header.Length);
        Assert.True(Guid.TryParseExact(header, "N", out _));
        Assert.Equal(header, body?.CorrelationId);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task UnsupportedMethod_ReturnsSafeMethodNotAllowedProblemDetails(string path)
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(405, document.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("Exception", content, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task UnknownHealthResource_ReturnsSafeNotFoundProblemDetails()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/health/not-a-resource");
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, document.RootElement.GetProperty("status").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("correlationId", out _));
        Assert.DoesNotContain("stack", content, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task RepeatedGet_IsSafeAndIdempotent(string path)
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var first = await client.GetAsync(path);
        using var second = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    private sealed record HealthResponseContract(string Status, string CorrelationId);
}
