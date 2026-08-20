using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class GoogleAuthenticationEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string GoogleEndpoint = "/api/v1/auth/google";
    private const string ChallengeEndpoint = "/api/v1/auth/email/challenges";
    private const string VerifyEndpoint = "/api/v1/auth/email/verify";
    private const string RefreshEndpoint = "/api/v1/auth/refresh";
    private const string LogoutEndpoint = "/api/v1/auth/logout";

    [Fact]
    public async Task GoogleDisabled_ReturnsServiceUnavailableWhileEmailAuthenticationStillWorks()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var google = await client.PostAsJsonAsync(
            GoogleEndpoint,
            new { credential = "google-id-token" });
        var emailAuthentication = await AuthenticateWithEmailAsync(
            factory,
            client,
            $"google-disabled-{UniqueSuffix()}@example.com");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, google.StatusCode);
        Assert.NotEmpty(emailAuthentication.AccessToken);
        Assert.NotEmpty(emailAuthentication.RefreshToken);
    }

    [Fact]
    public async Task InvalidGoogleRequest_UsesSafeRequestSemantics()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var missing = await client.PostAsJsonAsync(
            GoogleEndpoint,
            new { credential = (string?)null });
        using var malformed = await client.PostAsync(
            GoogleEndpoint,
            new StringContent("{\"credential\":", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [Fact]
    public async Task EnabledProductionAdapter_RejectsMalformedIdToken()
    {
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Authentication:Google:Enabled"] = "true",
                ["Authentication:Google:ClientId"] =
                    "integration-test.apps.googleusercontent.com"
            });
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            GoogleEndpoint,
            new { credential = "not-a-google-id-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("invalid-signature")]
    [InlineData("expired")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-issuer")]
    [InlineData("malformed-token")]
    public async Task InvalidGoogleCredentials_ReturnGenericUnauthorized(string credential)
    {
        var provider = new StubExternalIdentityProvider();
        provider.Reject(credential);
        using var logger = new InMemoryLoggerProvider();
        using var factory = CreateGoogleFactory(provider, logger);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            GoogleEndpoint,
            new { credential });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(credential, body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            credential,
            string.Join(Environment.NewLine, logger.Messages),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderUnavailable_ReturnsSafeServiceUnavailable()
    {
        const string credential = "temporarily-unavailable-google-token";
        var provider = new StubExternalIdentityProvider();
        provider.MakeUnavailable(credential);
        using var factory = CreateGoogleFactory(provider);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            GoogleEndpoint,
            new { credential });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(credential, body, StringComparison.Ordinal);
        Assert.DoesNotContain("googleusercontent.com", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HttpRequestException", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewGoogleUser_AtomicallyProvisionsIdentityAndUsesBeeexySessions()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var email = $"google-new-{suffix}@example.com";
        var credential = $"google-credential-{suffix}";
        var provider = new StubExternalIdentityProvider();
        provider.Accept(credential, $"google-subject-{suffix}", email);
        using var logger = new InMemoryLoggerProvider();
        using var factory = CreateGoogleFactory(provider, logger);
        using var client = factory.CreateApiClient();

        var first = await AuthenticateWithGoogleAsync(client, credential, HttpStatusCode.OK);
        Assert.NotNull(first);
        var rotated = await RefreshAsync(client, first.RefreshToken, HttpStatusCode.OK);
        Assert.NotNull(rotated);
        Assert.NotEqual(first.RefreshToken, rotated.RefreshToken);

        using var reused = await client.PostAsJsonAsync(
            RefreshEndpoint,
            new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);

        var logoutSession = await AuthenticateWithGoogleAsync(
            client,
            credential,
            HttpStatusCode.OK);
        Assert.NotNull(logoutSession);
        using var logout = await PostLogoutAsync(client, logoutSession.AccessToken);
        using var refreshAfterLogout = await client.PostAsJsonAsync(
            RefreshEndpoint,
            new { refreshToken = logoutSession.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);

        await using var dbContext = CreateDbContext();
        var normalizedEmail = NormalizedEmail.Create(email);
        var account = await dbContext.Accounts.SingleAsync(x => x.Email == normalizedEmail);
        Assert.Equal(first.Account.AccountId, account.Id.Value);
        Assert.Equal(1, await dbContext.Accounts.CountAsync(x => x.Email == normalizedEmail));
        Assert.Equal(1, await dbContext.PatientProfiles.CountAsync(x => x.AccountId == account.Id));
        Assert.Equal(1, await dbContext.UserPreferences.CountAsync(x => x.AccountId == account.Id));
        var identity = await dbContext.ExternalIdentities.SingleAsync(
            x => x.Provider == "google" && x.Subject == $"google-subject-{suffix}");
        Assert.Equal(account.Id, identity.AccountId);
        Assert.True(await dbContext.RefreshSessions.CountAsync(x => x.AccountId == account.Id) >= 3);

        var logs = string.Join(Environment.NewLine, logger.Messages);
        Assert.DoesNotContain(credential, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(first.AccessToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(first.RefreshToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(credential, identity.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingGoogleIdentity_ReusesIdentityAccountProfileAndPreference()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var email = $"google-existing-{suffix}@example.com";
        var credential = $"google-existing-credential-{suffix}";
        var provider = new StubExternalIdentityProvider();
        provider.Accept(credential, $"google-existing-subject-{suffix}", email);
        using var factory = CreateGoogleFactory(provider);
        using var client = factory.CreateApiClient();

        var first = await AuthenticateWithGoogleAsync(client, credential, HttpStatusCode.OK);
        var second = await AuthenticateWithGoogleAsync(client, credential, HttpStatusCode.OK);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Account, second.Account);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        await using var dbContext = CreateDbContext();
        var accountId = EntityId.From(first.Account.AccountId);
        Assert.Equal(1, await dbContext.Accounts.CountAsync(x => x.Id == accountId));
        Assert.Equal(1, await dbContext.PatientProfiles.CountAsync(x => x.AccountId == accountId));
        Assert.Equal(1, await dbContext.UserPreferences.CountAsync(x => x.AccountId == accountId));
        Assert.Equal(1, await dbContext.ExternalIdentities.CountAsync(x => x.AccountId == accountId));
        Assert.Equal(2, await dbContext.RefreshSessions.CountAsync(x => x.AccountId == accountId));
    }

    [Fact]
    public async Task EmailThenGoogle_ConvergesOnOneBeeexyAccount()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var email = $"email-google-{suffix}@example.com";
        var credential = $"email-google-credential-{suffix}";
        var provider = new StubExternalIdentityProvider();
        provider.Accept(credential, $"email-google-subject-{suffix}", email.ToUpperInvariant());
        using var factory = CreateGoogleFactory(provider);
        using var client = factory.CreateApiClient();

        var emailResult = await AuthenticateWithEmailAsync(factory, client, email);
        var googleResult = await AuthenticateWithGoogleAsync(client, credential, HttpStatusCode.OK);

        Assert.NotNull(googleResult);
        Assert.Equal(emailResult.Account, googleResult.Account);
        await AssertConvergedIdentityAsync(email, googleResult.Account.AccountId, expectedSessions: 2);
    }

    [Fact]
    public async Task GoogleThenEmail_ConvergesOnOneBeeexyAccount()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var email = $"google-email-{suffix}@example.com";
        var credential = $"google-email-credential-{suffix}";
        var provider = new StubExternalIdentityProvider();
        provider.Accept(credential, $"google-email-subject-{suffix}", email);
        using var factory = CreateGoogleFactory(provider);
        using var client = factory.CreateApiClient();

        var googleResult = await AuthenticateWithGoogleAsync(client, credential, HttpStatusCode.OK);
        var emailResult = await AuthenticateWithEmailAsync(factory, client, email.ToUpperInvariant());

        Assert.NotNull(googleResult);
        Assert.Equal(googleResult.Account, emailResult.Account);
        await AssertConvergedIdentityAsync(email, googleResult.Account.AccountId, expectedSessions: 2);
    }

    [Fact]
    public async Task ExistingIdentityAndDifferentEmailAccountConflict_FailsWithoutRelinking()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var firstEmail = $"identity-owner-{suffix}@example.com";
        var secondEmail = $"identity-conflict-{suffix}@example.com";
        var credential = $"identity-conflict-credential-{suffix}";
        var subject = $"identity-conflict-subject-{suffix}";
        var provider = new StubExternalIdentityProvider();
        provider.Accept(credential, subject, secondEmail);
        using var factory = CreateGoogleFactory(provider);
        using var client = factory.CreateApiClient();
        var first = await AuthenticateWithEmailAsync(factory, client, firstEmail);
        var second = await AuthenticateWithEmailAsync(factory, client, secondEmail);
        await SaveAsync(ExternalIdentity.Create(
            EntityId.From(first.Account.AccountId),
            "google",
            subject,
            DateTimeOffset.UtcNow));

        using var response = await client.PostAsJsonAsync(
            GoogleEndpoint,
            new { credential });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var dbContext = CreateDbContext();
        var identity = await dbContext.ExternalIdentities.SingleAsync(
            x => x.Provider == "google" && x.Subject == subject);
        Assert.Equal(EntityId.From(first.Account.AccountId), identity.AccountId);
        Assert.NotEqual(EntityId.From(second.Account.AccountId), identity.AccountId);
    }

    [Fact]
    public async Task UnverifiedEmailCannotCreateIdentityButKnownSubjectCanAuthenticate()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var email = $"unverified-google-{suffix}@example.com";
        var credential = $"unverified-google-credential-{suffix}";
        var subject = $"unverified-google-subject-{suffix}";
        var provider = new StubExternalIdentityProvider();
        provider.Accept(credential, subject, email, emailVerified: false);
        using var factory = CreateGoogleFactory(provider);
        using var client = factory.CreateApiClient();

        using (var unknown = await client.PostAsJsonAsync(GoogleEndpoint, new { credential }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        }

        provider.Accept(credential, subject, email, emailVerified: true);
        var created = await AuthenticateWithGoogleAsync(client, credential, HttpStatusCode.OK);
        provider.Accept(credential, subject, email, emailVerified: false);
        var known = await AuthenticateWithGoogleAsync(client, credential, HttpStatusCode.OK);

        Assert.NotNull(created);
        Assert.NotNull(known);
        Assert.Equal(created.Account, known.Account);
    }

    [Fact]
    public async Task DisabledBeeexyAccountCannotAuthenticateWithKnownGoogleIdentity()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var email = $"disabled-google-{suffix}@example.com";
        var credential = $"disabled-google-credential-{suffix}";
        var provider = new StubExternalIdentityProvider();
        provider.Accept(credential, $"disabled-google-subject-{suffix}", email);
        using var factory = CreateGoogleFactory(provider);
        using var client = factory.CreateApiClient();
        var created = await AuthenticateWithGoogleAsync(client, credential, HttpStatusCode.OK);
        Assert.NotNull(created);

        await using (var dbContext = CreateDbContext())
        {
            var account = await dbContext.Accounts.SingleAsync(
                x => x.Id == EntityId.From(created.Account.AccountId));
            account.Disable(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        using var response = await client.PostAsJsonAsync(GoogleEndpoint, new { credential });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using var verifyContext = CreateDbContext();
        Assert.Equal(
            1,
            await verifyContext.RefreshSessions.CountAsync(
                x => x.AccountId == EntityId.From(created.Account.AccountId)));
    }

    [Fact]
    public async Task ConcurrentFirstGoogleAuthentications_CreateOneIdentityBranch()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var email = $"google-race-{suffix}@example.com";
        var credential = $"google-race-credential-{suffix}";
        var subject = $"google-race-subject-{suffix}";
        var provider = new StubExternalIdentityProvider();
        provider.Accept(credential, subject, email);
        using var factory = CreateGoogleFactory(provider);
        using var client = factory.CreateApiClient();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () =>
            {
                await gate.Task;
                return await AuthenticateWithGoogleAsync(client, credential, HttpStatusCode.OK);
            }))
            .ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(attempts);

        Assert.All(results, Assert.NotNull);
        var accountId = results[0]!.Account.AccountId;
        Assert.Single(results.Select(x => x!.Account.AccountId).Distinct());
        Assert.Single(results.Select(x => x!.Account.ProfileId).Distinct());

        await using var dbContext = CreateDbContext();
        var entityAccountId = EntityId.From(accountId);
        Assert.Equal(1, await dbContext.Accounts.CountAsync(x => x.Email == NormalizedEmail.Create(email)));
        Assert.Equal(1, await dbContext.PatientProfiles.CountAsync(x => x.AccountId == entityAccountId));
        Assert.Equal(1, await dbContext.UserPreferences.CountAsync(x => x.AccountId == entityAccountId));
        Assert.Equal(1, await dbContext.ExternalIdentities.CountAsync(
            x => x.Provider == "google" && x.Subject == subject));
        Assert.Equal(8, await dbContext.RefreshSessions.CountAsync(x => x.AccountId == entityAccountId));
    }

    private BeeexyApiFactory CreateGoogleFactory(
        StubExternalIdentityProvider provider,
        InMemoryLoggerProvider? logger = null)
    {
        return new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: logger,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Authentication:Google:Enabled"] = "true",
                ["Authentication:Google:ClientId"] = "integration-test.apps.googleusercontent.com"
            },
            configureServices: services =>
            {
                services.RemoveAll<IExternalIdentityProvider>();
                services.AddSingleton<IExternalIdentityProvider>(provider);
            });
    }

    private static async Task<AuthenticationResponse?> AuthenticateWithGoogleAsync(
        HttpClient client,
        string credential,
        HttpStatusCode expectedStatus)
    {
        using var response = await client.PostAsJsonAsync(GoogleEndpoint, new { credential });
        Assert.Equal(expectedStatus, response.StatusCode);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AuthenticationResponse>()
            : null;
    }

    private static async Task<AuthenticationResponse?> RefreshAsync(
        HttpClient client,
        string refreshToken,
        HttpStatusCode expectedStatus)
    {
        using var response = await client.PostAsJsonAsync(
            RefreshEndpoint,
            new { refreshToken });
        Assert.Equal(expectedStatus, response.StatusCode);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AuthenticationResponse>()
            : null;
    }

    private static Task<HttpResponseMessage> PostLogoutAsync(
        HttpClient client,
        string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, LogoutEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request);
    }

    private static async Task<AuthenticationResponse> AuthenticateWithEmailAsync(
        BeeexyApiFactory factory,
        HttpClient client,
        string email)
    {
        var sender = factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>();
        var previousCount = sender.Messages.Count;
        using var challenge = await client.PostAsJsonAsync(ChallengeEndpoint, new { email });
        Assert.Equal(HttpStatusCode.Accepted, challenge.StatusCode);
        Assert.Equal(previousCount + 1, sender.Messages.Count);
        var code = sender.Messages.Last().OneTimeCode;
        using var verification = await client.PostAsJsonAsync(
            VerifyEndpoint,
            new { email, code });
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        return (await verification.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private async Task AssertConvergedIdentityAsync(
        string email,
        Guid accountId,
        int expectedSessions)
    {
        await using var dbContext = CreateDbContext();
        var normalizedEmail = NormalizedEmail.Create(email);
        var entityAccountId = EntityId.From(accountId);
        Assert.Equal(1, await dbContext.Accounts.CountAsync(x => x.Email == normalizedEmail));
        Assert.Equal(1, await dbContext.PatientProfiles.CountAsync(x => x.AccountId == entityAccountId));
        Assert.Equal(1, await dbContext.UserPreferences.CountAsync(x => x.AccountId == entityAccountId));
        Assert.Equal(1, await dbContext.ExternalIdentities.CountAsync(x => x.AccountId == entityAccountId));
        Assert.Equal(expectedSessions, await dbContext.RefreshSessions.CountAsync(
            x => x.AccountId == entityAccountId));
    }

    private BeeexyDbContext CreateDbContext()
    {
        return new BeeexyDbContext(
            new DbContextOptionsBuilder<BeeexyDbContext>()
                .UseNpgsql(postgres.ConnectionString)
                .Options);
    }

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private async Task SaveAsync(params object[] entities)
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(entities);
        await dbContext.SaveChangesAsync();
    }

    private static string UniqueSuffix() => Guid.NewGuid().ToString("N");

    private sealed record AuthenticationResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt,
        DateTimeOffset RefreshTokenExpiresAt,
        AccountResponse Account);

    private sealed record AccountResponse(Guid AccountId, Guid ProfileId, string BeeexyId);
}
