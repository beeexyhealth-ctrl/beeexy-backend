using System.Net;
using System.Text;
using System.Text.Json;
using Beeexy.Tests.Integration.Support;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProblemDetailsAndLoggingTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task NotFoundProblemDetails_DoesNotLeakInternalsOrConfiguration()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/not-found");
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Not Found", document.RootElement.GetProperty("title").GetString());
        Assert.True(document.RootElement.TryGetProperty("correlationId", out _));
        Assert.DoesNotContain("Password", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestBodyAndBearerToken_AreNotLoggedByDefault()
    {
        const string bodySecret = "request-body-secret-45108";
        const string tokenSecret = "bearer-token-secret-98217";
        using var loggerProvider = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: loggerProvider);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/health/live")
        {
            Content = new StringContent(bodySecret, Encoding.UTF8, "text/plain")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            tokenSecret);

        using var response = await client.SendAsync(request);
        var combinedLogs = string.Join(Environment.NewLine, loggerProvider.Messages);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.NotEmpty(loggerProvider.Messages);
        Assert.Contains("/health/live", combinedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(bodySecret, combinedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(tokenSecret, combinedLogs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableDatabase_ConnectionSecretIsNotLoggedOrReturned()
    {
        const string password = "database-password-secret-78341";
        var connectionString =
            "Host=127.0.0.1;Port=1;Database=private_database;Username=private_user;" +
            $"Password={password};Timeout=1;Command Timeout=1;Include Error Detail=false";
        using var loggerProvider = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            connectionString,
            environment: "Test",
            loggerProvider: loggerProvider);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/health/ready");
        var responseContent = await response.Content.ReadAsStringAsync();
        var combinedLogs = string.Join(Environment.NewLine, loggerProvider.Messages);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotEmpty(loggerProvider.Messages);
        Assert.DoesNotContain(password, responseContent, StringComparison.Ordinal);
        Assert.DoesNotContain(password, combinedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, combinedLogs, StringComparison.Ordinal);
    }
}
