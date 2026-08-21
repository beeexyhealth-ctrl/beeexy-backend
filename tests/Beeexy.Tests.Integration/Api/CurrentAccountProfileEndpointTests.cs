using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class CurrentAccountProfileEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string ChallengeEndpoint = "/api/v1/auth/email/challenges";
    private const string VerifyEndpoint = "/api/v1/auth/email/verify";
    private const string GoogleEndpoint = "/api/v1/auth/google";
    private const string RefreshEndpoint = "/api/v1/auth/refresh";
    private const string LogoutEndpoint = "/api/v1/auth/logout";
    private const string AccountEndpoint = "/api/v1/auth/me";
    private const string ProfileEndpoint = "/api/v1/patients/me";
    private const string SigningKey =
        "integration-test-only-jwt-signing-key-with-at-least-32-bytes";

    [Fact]
    public async Task UnauthenticatedExpiredAndBeeexyIdCredentials_CannotAccessMeEndpoints()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateWithEmailAsync(
            factory,
            client,
            $"profile-auth-{UniqueSuffix()}@example.com");
        var expired = CreateExpiredJwt(authentication.Account.AccountId);

        using var accountWithoutBearer = await client.GetAsync(AccountEndpoint);
        using var profileWithoutBearer = await client.GetAsync(ProfileEndpoint);
        using var expiredBearer = await GetWithBearerAsync(client, ProfileEndpoint, expired);
        using var beeexyIdBearer = await GetWithBearerAsync(
            client,
            ProfileEndpoint,
            authentication.Account.BeeexyId);

        Assert.Equal(HttpStatusCode.Unauthorized, accountWithoutBearer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, profileWithoutBearer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, expiredBearer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, beeexyIdBearer.StatusCode);
    }

    [Fact]
    public async Task EmailAccount_EndToEndReadsUpdatesRefreshesAndLogsOutWithoutSensitiveLogging()
    {
        await EnsureMigratedAsync();
        using var logger = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: logger);
        using var client = factory.CreateApiClient();
        var email = $"profile-email-{UniqueSuffix()}@example.com";
        var authentication = await AuthenticateWithEmailAsync(factory, client, email);

        using var accountResponse = await GetWithBearerAsync(
            client,
            AccountEndpoint,
            authentication.AccessToken);
        var accountBody = await accountResponse.Content.ReadAsStringAsync();
        var account = await accountResponse.Content.ReadFromJsonAsync<CurrentAccountResponse>();
        Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);
        Assert.NotNull(account);
        Assert.Equal(authentication.Account.AccountId, account.AccountId);
        Assert.Equal("active", account.Status);
        Assert.Equal(authentication.Account.ProfileId, account.PrimaryProfile.ProfileId);
        Assert.Equal(authentication.Account.BeeexyId, account.PrimaryProfile.BeeexyId);
        Assert.Equal("Etc/UTC", account.Preferences.Timezone);
        Assert.DoesNotContain(email, accountBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", accountBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session", accountBody, StringComparison.OrdinalIgnoreCase);

        var profile = await GetProfileAsync(client, authentication.AccessToken);
        Assert.Equal(authentication.Account.ProfileId, profile.ProfileId);
        Assert.Equal(authentication.Account.BeeexyId, profile.BeeexyId);
        Assert.Null(profile.FirstName);
        Assert.Null(profile.LastName);
        Assert.Null(profile.DateOfBirth);
        Assert.Null(profile.SexAssignedAtBirth);
        Assert.Null(profile.State);
        Assert.Equal(1, profile.ProfileVersion);
        Assert.Equal("Etc/UTC", profile.Preferences.Timezone);
        Assert.Equal(1, profile.Version);

        var updated = await PatchProfileAsync(
            client,
            authentication.AccessToken,
            new { timezone = "America/Lima", version = profile.Version },
            HttpStatusCode.OK);
        Assert.NotNull(updated);
        Assert.Equal("America/Lima", updated.Preferences.Timezone);
        Assert.Equal(2, updated.Version);

        var noOp = await PatchProfileAsync(
            client,
            authentication.AccessToken,
            new { timezone = "America/Lima", version = updated.Version },
            HttpStatusCode.OK);
        Assert.NotNull(noOp);
        Assert.Equal("America/Lima", noOp.Preferences.Timezone);
        Assert.Equal(2, noOp.Version);

        var afterUpdate = await GetProfileAsync(client, authentication.AccessToken);
        Assert.Equal(updated, afterUpdate);
        var rotated = await RefreshAsync(client, authentication.RefreshToken);
        var throughRotatedAccess = await GetProfileAsync(client, rotated.AccessToken);
        Assert.Equal(afterUpdate, throughRotatedAccess);

        using var logout = await PostLogoutAsync(client, rotated.AccessToken);
        using var refreshAfterLogout = await client.PostAsJsonAsync(
            RefreshEndpoint,
            new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);

        var logs = string.Join(Environment.NewLine, logger.Messages);
        Assert.DoesNotContain("America/Lima", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Etc/UTC", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(authentication.AccessToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(authentication.RefreshToken, logs, StringComparison.Ordinal);
        Assert.Contains("changed fields timezone", logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GoogleAccount_UsesIdenticalMeUpdateRefreshAndLogoutSemantics()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var credential = $"profile-google-credential-{suffix}";
        var provider = new StubExternalIdentityProvider();
        provider.Accept(credential, $"profile-google-subject-{suffix}", $"profile-google-{suffix}@example.com");
        using var factory = CreateGoogleFactory(provider);
        using var client = factory.CreateApiClient();

        using var googleResponse = await client.PostAsJsonAsync(
            GoogleEndpoint,
            new { credential });
        var authentication = await googleResponse.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.Equal(HttpStatusCode.OK, googleResponse.StatusCode);
        Assert.NotNull(authentication);

        using var accountResponse = await GetWithBearerAsync(
            client,
            AccountEndpoint,
            authentication.AccessToken);
        Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);
        var profile = await GetProfileAsync(client, authentication.AccessToken);
        var updated = await PatchProfileAsync(
            client,
            authentication.AccessToken,
            new { timezone = "America/New_York", version = profile.Version },
            HttpStatusCode.OK);
        Assert.NotNull(updated);
        Assert.Equal("America/New_York", updated.Preferences.Timezone);

        var rotated = await RefreshAsync(client, authentication.RefreshToken);
        Assert.Equal(updated, await GetProfileAsync(client, rotated.AccessToken));
        using var logout = await PostLogoutAsync(client, rotated.AccessToken);
        using var afterLogout = await client.PostAsJsonAsync(
            RefreshEndpoint,
            new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task PostgreSqlStaleVersion_ReturnsConflictWithoutOverwritingSuccessfulUpdate()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateWithEmailAsync(
            factory,
            client,
            $"profile-stale-{UniqueSuffix()}@example.com");
        var original = await GetProfileAsync(client, authentication.AccessToken);

        var accepted = await PatchProfileAsync(
            client,
            authentication.AccessToken,
            new { timezone = "America/Lima", version = original.Version },
            HttpStatusCode.OK);
        using var staleResponse = await SendPatchWithBearerAsync(
            client,
            authentication.AccessToken,
            new { timezone = "America/New_York", version = original.Version });

        Assert.NotNull(accepted);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        var persisted = await GetProfileAsync(client, authentication.AccessToken);
        Assert.Equal("America/Lima", persisted.Preferences.Timezone);
        Assert.Equal(original.Version + 1, persisted.Version);

        await using var dbContext = CreateDbContext();
        var preference = await dbContext.UserPreferences.AsNoTracking().SingleAsync(
            candidate => candidate.AccountId == EntityId.From(authentication.Account.AccountId));
        Assert.Equal("America/Lima", preference.TimeZone.Value);
        Assert.Equal(original.Version + 1, preference.Version);
    }

    [Fact]
    public async Task ConcurrentPostgreSqlUpdatesWithSameVersion_OneSucceedsAndOneConflicts()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateWithEmailAsync(
            factory,
            client,
            $"profile-concurrent-{UniqueSuffix()}@example.com");
        var original = await GetProfileAsync(client, authentication.AccessToken);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = new[] { "America/Lima", "America/New_York" }
            .Select(timezone => Task.Run(async () =>
            {
                await gate.Task;
                return await SendPatchWithBearerAsync(
                    client,
                    authentication.AccessToken,
                    new { timezone, version = original.Version });
            }))
            .ToArray();
        gate.SetResult();
        var responses = await Task.WhenAll(attempts);
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var persisted = await GetProfileAsync(client, authentication.AccessToken);
        Assert.Contains(
            persisted.Preferences.Timezone,
            new[] { "America/Lima", "America/New_York" });
        Assert.Equal(original.Version + 1, persisted.Version);
    }

    [Fact]
    public async Task MeRoutes_DeriveOwnershipOnlyFromBearerAccount()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var accountA = await AuthenticateWithEmailAsync(
            factory,
            client,
            $"profile-owner-a-{UniqueSuffix()}@example.com");
        var accountB = await AuthenticateWithEmailAsync(
            factory,
            client,
            $"profile-owner-b-{UniqueSuffix()}@example.com");

        var attemptedTarget =
            $"{ProfileEndpoint}?accountId={accountB.Account.AccountId:D}" +
            $"&profileId={accountB.Account.ProfileId:D}" +
            $"&beeexyId={Uri.EscapeDataString(accountB.Account.BeeexyId)}";
        using var targetedRead = await GetWithBearerAsync(
            client,
            attemptedTarget,
            accountA.AccessToken);
        var returned = await targetedRead.Content.ReadFromJsonAsync<PrimaryProfileResponse>();
        Assert.Equal(HttpStatusCode.OK, targetedRead.StatusCode);
        Assert.NotNull(returned);
        Assert.Equal(accountA.Account.ProfileId, returned.ProfileId);
        Assert.NotEqual(accountB.Account.ProfileId, returned.ProfileId);

        var updatedA = await PatchProfileAtAsync(
            client,
            attemptedTarget,
            accountA.AccessToken,
            new { timezone = "America/Lima", version = returned.Version },
            HttpStatusCode.OK);
        Assert.NotNull(updatedA);
        Assert.Equal(accountA.Account.ProfileId, updatedA.ProfileId);
        Assert.Equal("Etc/UTC", (await GetProfileAsync(client, accountB.AccessToken)).Preferences.Timezone);

        using var immutableTargetAttempt = await SendPatchWithBearerAsync(
            client,
            accountA.AccessToken,
            new
            {
                timezone = "America/New_York",
                version = updatedA.Version,
                accountId = accountB.Account.AccountId,
                profileId = accountB.Account.ProfileId,
                beeexyId = accountB.Account.BeeexyId
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, immutableTargetAttempt.StatusCode);
        Assert.Equal(
            "America/Lima",
            (await GetProfileAsync(client, accountA.AccessToken)).Preferences.Timezone);
        Assert.Equal(
            "Etc/UTC",
            (await GetProfileAsync(client, accountB.AccessToken)).Preferences.Timezone);

        using var byBeeexyId = await GetWithBearerAsync(
            client,
            $"/api/v1/patients/{accountB.Account.BeeexyId}",
            accountA.AccessToken);
        using var byProfileId = await GetWithBearerAsync(
            client,
            $"/api/v1/patients/{accountB.Account.ProfileId:D}",
            accountA.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, byBeeexyId.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byProfileId.StatusCode);
    }

    [Fact]
    public async Task DisabledAccountCannotReadOrUpdateProfile()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateWithEmailAsync(
            factory,
            client,
            $"profile-disabled-{UniqueSuffix()}@example.com");
        await using (var dbContext = CreateDbContext())
        {
            var account = await dbContext.Accounts.SingleAsync(
                candidate => candidate.Id == EntityId.From(authentication.Account.AccountId));
            account.Disable(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        using var accountRead = await GetWithBearerAsync(
            client,
            AccountEndpoint,
            authentication.AccessToken);
        using var profileRead = await GetWithBearerAsync(
            client,
            ProfileEndpoint,
            authentication.AccessToken);
        using var profileUpdate = await SendPatchWithBearerAsync(
            client,
            authentication.AccessToken,
            new { timezone = "America/Lima", version = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, accountRead.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, profileRead.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, profileUpdate.StatusCode);
    }

    [Fact]
    public async Task MissingMvpProfile_ReturnsAuditedSafeInvariantFailure()
    {
        await EnsureMigratedAsync();
        using var logger = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: logger);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateWithEmailAsync(
            factory,
            client,
            $"profile-invariant-{UniqueSuffix()}@example.com");
        await using (var dbContext = CreateDbContext())
        {
            var profile = await dbContext.PatientProfiles.SingleAsync(
                candidate => candidate.Id == EntityId.From(authentication.Account.ProfileId));
            dbContext.PatientProfiles.Remove(profile);
            await dbContext.SaveChangesAsync();
        }

        using var accountRead = await GetWithBearerAsync(
            client,
            AccountEndpoint,
            authentication.AccessToken);
        using var profileRead = await GetWithBearerAsync(
            client,
            ProfileEndpoint,
            authentication.AccessToken);
        var accountBody = await accountRead.Content.ReadAsStringAsync();
        var profileBody = await profileRead.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, accountRead.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, profileRead.StatusCode);
        Assert.DoesNotContain("primary-profile-count", accountBody, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountProfileInvariantException", profileBody, StringComparison.Ordinal);
        Assert.Contains("primary-profile-count", string.Join(Environment.NewLine, logger.Messages));
    }

    [Theory]
    [InlineData("Not/A_Real_Zone", 1)]
    [InlineData("", 1)]
    [InlineData("America/Lima", 0)]
    public async Task InvalidProfileUpdate_ReturnsUnprocessableEntity(
        string timezone,
        long version)
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateWithEmailAsync(
            factory,
            client,
            $"profile-invalid-{UniqueSuffix()}@example.com");

        using var response = await SendPatchWithBearerAsync(
            client,
            authentication.AccessToken,
            new { timezone, version });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private BeeexyApiFactory CreateGoogleFactory(StubExternalIdentityProvider provider)
    {
        return new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Authentication:Google:Enabled"] = "true",
                ["Authentication:Google:ClientId"] =
                    "integration-test.apps.googleusercontent.com"
            },
            configureServices: services =>
            {
                services.RemoveAll<IExternalIdentityProvider>();
                services.AddSingleton<IExternalIdentityProvider>(provider);
            });
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
        using var verification = await client.PostAsJsonAsync(
            VerifyEndpoint,
            new { email, code = sender.Messages.Last().OneTimeCode });
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        return (await verification.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private static async Task<PrimaryProfileResponse> GetProfileAsync(
        HttpClient client,
        string accessToken)
    {
        using var response = await GetWithBearerAsync(client, ProfileEndpoint, accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrimaryProfileResponse>())!;
    }

    private static async Task<PrimaryProfileResponse?> PatchProfileAsync(
        HttpClient client,
        string accessToken,
        object request,
        HttpStatusCode expectedStatus) =>
        await PatchProfileAtAsync(client, ProfileEndpoint, accessToken, request, expectedStatus);

    private static async Task<PrimaryProfileResponse?> PatchProfileAtAsync(
        HttpClient client,
        string endpoint,
        string accessToken,
        object request,
        HttpStatusCode expectedStatus)
    {
        using var response = await SendPatchWithBearerAsync(
            client,
            accessToken,
            request,
            endpoint);
        Assert.Equal(expectedStatus, response.StatusCode);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<PrimaryProfileResponse>()
            : null;
    }

    private static async Task<AuthenticationResponse> RefreshAsync(
        HttpClient client,
        string refreshToken)
    {
        using var response = await client.PostAsJsonAsync(
            RefreshEndpoint,
            new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private static Task<HttpResponseMessage> GetWithBearerAsync(
        HttpClient client,
        string endpoint,
        string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendPatchWithBearerAsync(
        HttpClient client,
        string accessToken,
        object body,
        string endpoint = ProfileEndpoint)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, endpoint)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostLogoutAsync(
        HttpClient client,
        string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, LogoutEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request);
    }

    private static string CreateExpiredJwt(Guid accountId)
    {
        var now = DateTimeOffset.UtcNow;
        var token = new JwtSecurityToken(
            "https://api.beeexy.com",
            "beeexy-client",
            [
                new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString("D")),
                new Claim("sid", Guid.NewGuid().ToString("D"))
            ],
            now.AddMinutes(-2).UtcDateTime,
            now.AddMinutes(-1).UtcDateTime,
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task EnsureMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private static string UniqueSuffix() => Guid.NewGuid().ToString("N");

    private sealed record AuthenticationResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt,
        DateTimeOffset RefreshTokenExpiresAt,
        AuthenticationAccountResponse Account);

    private sealed record AuthenticationAccountResponse(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);

    private sealed record CurrentAccountResponse(
        Guid AccountId,
        string Status,
        CurrentPrimaryProfileResponse PrimaryProfile,
        PreferencesResponse Preferences);

    private sealed record CurrentPrimaryProfileResponse(Guid ProfileId, string BeeexyId);

    private sealed record PrimaryProfileResponse(
        Guid ProfileId,
        string BeeexyId,
        string? FirstName,
        string? LastName,
        DateOnly? DateOfBirth,
        string? SexAssignedAtBirth,
        string? State,
        long ProfileVersion,
        PreferencesResponse Preferences,
        long Version);

    private sealed record PreferencesResponse(string Timezone);
}
