using System.Net;
using System.Net.Http.Json;
using System.Text;
using Beeexy.Api.PrivateAccess;
using Beeexy.Tests.Integration.Support;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class PrivateAccessEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string Username = "BeeexyHealth";
    private const string Password = "IntegrationPassword!123";
    private const string Keyword = "HealthTech";

    [Fact]
    public async Task CorrectCombination_EstablishesSession_AndLogoutRemovesIt()
    {
        using var factory = CreateEnabledFactory();
        using var client = factory.CreateApiClient();

        using var login = await client.PostAsJsonAsync(
            "/api/v1/private-access/login",
            new { username = Username, password = Password, keyword = Keyword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, cookie, StringComparison.Ordinal);
        Assert.DoesNotContain(Keyword, cookie, StringComparison.Ordinal);

        using var session = await client.GetAsync("/api/v1/private-access/session");
        var status = await session.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.True(status!.Authenticated);
        Assert.NotNull(status.ExpiresAt);

        using var product = await client.PostAsJsonAsync("/api/v1/auth/google", new { });
        Assert.DoesNotContain(
            "valid private demo access session",
            await product.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        using var logout = await client.PostAsync("/api/v1/private-access/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var afterLogout = await client.GetAsync("/api/v1/private-access/session");
        Assert.False((await afterLogout.Content.ReadFromJsonAsync<SessionResponse>())!.Authenticated);
    }

    [Theory]
    [InlineData("wrong-user", Password, Keyword)]
    [InlineData(Username, "wrong-password", Keyword)]
    [InlineData(Username, Password, "wrong-keyword")]
    public async Task WrongCredential_ReturnsSameGenericUnauthorized(
        string username,
        string password,
        string keyword)
    {
        using var factory = CreateEnabledFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/private-access/login",
            new { username, password, keyword });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("private access credentials are invalid", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(username, body, StringComparison.Ordinal);
        Assert.DoesNotContain(password, body, StringComparison.Ordinal);
        Assert.DoesNotContain(keyword, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedAndEmptyRequests_FailSafely()
    {
        using var factory = CreateEnabledFactory();
        using var client = factory.CreateApiClient();
        using var malformedContent = new StringContent(
            "{\"username\":",
            Encoding.UTF8,
            "application/json");

        using var malformed = await client.PostAsync(
            "/api/v1/private-access/login",
            malformedContent);
        using var empty = await client.PostAsJsonAsync(
            "/api/v1/private-access/login",
            new { username = "", password = "", keyword = "" });

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.DoesNotContain("JsonException", await malformed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EnabledGate_BlocksProductAndAuth_ButExemptsHealthBootstrapAndPreflight()
    {
        using var factory = CreateEnabledFactory();
        using var client = factory.CreateApiClient();

        using var product = await client.GetAsync("/api/v1/patients");
        using var auth = await client.PostAsJsonAsync("/api/v1/auth/google", new { });
        using var emailAuth = await client.PostAsJsonAsync(
            "/api/v1/auth/email/challenges",
            new { email = "gate-bypass@example.com" });
        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");
        using var session = await client.GetAsync("/api/v1/private-access/session");
        using var preflightRequest = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/v1/auth/google");
        preflightRequest.Headers.Add("Origin", BeeexyApiFactory.AllowedCorsOrigin);
        preflightRequest.Headers.Add("Access-Control-Request-Method", "POST");
        using var preflight = await client.SendAsync(preflightRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, product.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, auth.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, emailAuth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.True(preflight.IsSuccessStatusCode);
        Assert.Equal(
            BeeexyApiFactory.AllowedCorsOrigin,
            preflight.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", preflight.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    [Fact]
    public async Task DisabledGate_LeavesExistingApiBehaviorUnchanged()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/api/v1/patients");
        using var emailAuth = await client.PostAsJsonAsync(
            "/api/v1/auth/email/challenges",
            new { email = $"gate-disabled-{Guid.NewGuid():N}@example.com" });
        var session = await client.GetFromJsonAsync<SessionResponse>(
            "/api/v1/private-access/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, emailAuth.StatusCode);
        Assert.True(session!.Authenticated);
        Assert.Null(session.ExpiresAt);
    }

    [Fact]
    public async Task RepeatedAttempts_ReturnSafeRateLimitThatCorrectCredentialsCannotBypass()
    {
        using var factory = CreateEnabledFactory(new Dictionary<string, string?>
        {
            ["PrivateAccess:LoginPermitLimit"] = "2"
        });
        using var client = factory.CreateApiClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var rejected = await client.PostAsJsonAsync(
                "/api/v1/private-access/login",
                new { username = Username, password = "wrong", keyword = Keyword });
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync(
            "/api/v1/private-access/login",
            new { username = Username, password = Password, keyword = Keyword });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.DoesNotContain(Password, await limited.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TamperedCookie_IsRejectedAndRemoved()
    {
        using var factory = CreateEnabledFactory();
        using var loginClient = factory.CreateApiClient();
        using var login = await loginClient.PostAsJsonAsync(
            "/api/v1/private-access/login",
            new { username = Username, password = Password, keyword = Keyword });
        var cookiePair = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        var replacement = cookiePair[^1] == 'A' ? 'B' : 'A';
        var tamperedCookie = cookiePair[..^1] + replacement;
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients");
        request.Headers.TryAddWithoutValidation("Cookie", tamperedCookie);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SubmittedCredentialsAndConfiguredSecrets_AreNotLogged()
    {
        using var loggerProvider = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: loggerProvider,
            configurationOverrides: EnabledSettings());
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/private-access/login",
            new { username = Username, password = "submitted-secret", keyword = Keyword });
        var logs = string.Join(Environment.NewLine, loggerProvider.Messages);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(Username, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("submitted-secret", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(Keyword, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(
            EnabledSettings()["PrivateAccess:SessionSigningKey"]!,
            logs,
            StringComparison.Ordinal);
    }

    private BeeexyApiFactory CreateEnabledFactory(
        IReadOnlyDictionary<string, string?>? additional = null)
    {
        var settings = EnabledSettings();
        if (additional is not null)
        {
            foreach (var (key, value) in additional)
            {
                settings[key] = value;
            }
        }

        return new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: settings);
    }

    private static Dictionary<string, string?> EnabledSettings() => new()
    {
        ["PrivateAccess:Enabled"] = "true",
        ["PrivateAccess:Username"] = Username,
        ["PrivateAccess:PasswordHash"] = PrivateAccessPasswordHasher.Hash(Password),
        ["PrivateAccess:KeywordHash"] = PrivateAccessPasswordHasher.Hash(Keyword),
        ["PrivateAccess:SessionSigningKey"] = Convert.ToBase64String(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()),
        ["PrivateAccess:SessionLifetimeMinutes"] = "30",
        ["PrivateAccess:LoginPermitLimit"] = "5",
        ["PrivateAccess:LoginRateLimitWindowMinutes"] = "15"
    };

    private sealed record SessionResponse(bool Authenticated, DateTimeOffset? ExpiresAt);
}
