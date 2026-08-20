using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class AuthenticationSessionEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string ChallengeEndpoint = "/api/v1/auth/email/challenges";
    private const string VerifyEndpoint = "/api/v1/auth/email/verify";
    private const string RefreshEndpoint = "/api/v1/auth/refresh";
    private const string LogoutEndpoint = "/api/v1/auth/logout";
    private const string Issuer = "https://api.beeexy.com";
    private const string Audience = "beeexy-client";
    private const string SigningKey =
        "integration-test-only-jwt-signing-key-with-at-least-32-bytes";

    [Fact]
    public async Task AccessToken_HasExpectedClaimsAndBearerValidationRejectsInvalidVariants()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(
            factory,
            client,
            $"jwt-{UniqueSuffix()}@example.com");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(authentication.AccessToken);

        Assert.Equal("HS256", jwt.Header.Alg);
        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Equal([Audience], jwt.Audiences);
        Assert.Equal(authentication.Account.AccountId.ToString("D"), jwt.Subject);
        Assert.True(Guid.TryParse(jwt.Claims.Single(claim => claim.Type == "sid").Value, out _));
        Assert.NotNull(jwt.Claims.SingleOrDefault(claim => claim.Type == JwtRegisteredClaimNames.Iat));
        Assert.InRange(
            (authentication.AccessTokenExpiresAt - new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero)).Duration(),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));
        Assert.InRange(
            authentication.AccessTokenExpiresAt - DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(14),
            TimeSpan.FromMinutes(15));
        Assert.DoesNotContain(jwt.Claims, claim =>
            claim.Type.Contains("email", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Contains("beeexy", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Contains("patient", StringComparison.OrdinalIgnoreCase) ||
            claim.Value == authentication.Account.BeeexyId);

        var accountId = authentication.Account.AccountId;
        var sessionId = Guid.Parse(jwt.Claims.Single(claim => claim.Type == "sid").Value);
        var invalidTokens = new[]
        {
            CreateJwt(Issuer, Audience, "wrong-test-signing-key-with-at-least-32-bytes", accountId, sessionId, DateTimeOffset.UtcNow.AddMinutes(5)),
            CreateJwt("wrong-issuer", Audience, SigningKey, accountId, sessionId, DateTimeOffset.UtcNow.AddMinutes(5)),
            CreateJwt(Issuer, "wrong-audience", SigningKey, accountId, sessionId, DateTimeOffset.UtcNow.AddMinutes(5)),
            CreateJwt(Issuer, Audience, SigningKey, accountId, sessionId, DateTimeOffset.UtcNow.AddMinutes(-1))
        };

        foreach (var token in invalidTokens)
        {
            using var response = await PostLogoutAsync(client, token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task CompleteSessionFlow_RotatesDetectsReuseRevokesFamilyThenLogsOutIndependentSession()
    {
        await EnsureMigratedAsync();
        var email = $"session-flow-{UniqueSuffix()}@example.com";
        using var loggerProvider = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: loggerProvider);
        using var client = factory.CreateApiClient();

        var first = await AuthenticateAsync(factory, client, email);
        var second = await RefreshAsync(client, first.RefreshToken, HttpStatusCode.OK);
        var third = await RefreshAsync(client, second!.RefreshToken, HttpStatusCode.OK);

        Assert.NotEqual(first.AccessToken, second!.AccessToken);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(second.RefreshToken, third!.RefreshToken);

        using var reused = await client.PostAsJsonAsync(
            RefreshEndpoint,
            new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        using var descendant = await client.PostAsJsonAsync(
            RefreshEndpoint,
            new { refreshToken = third.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, descendant.StatusCode);

        await using (var dbContext = CreateDbContext())
        {
            var accountId = EntityId.From(first.Account.AccountId);
            var sessions = await dbContext.RefreshSessions
                .AsNoTracking()
                .Where(session => session.AccountId == accountId)
                .OrderBy(session => session.CreatedAt)
                .ToListAsync();
            Assert.Equal(3, sessions.Count);
            Assert.Single(sessions.Select(session => session.FamilyId).Distinct());
            Assert.All(sessions, session => Assert.Equal(RefreshSessionStatus.Revoked, session.Status));
            Assert.Equal(sessions[1].Id, sessions[0].ReplacedBySessionId);
            Assert.Equal(sessions[2].Id, sessions[1].ReplacedBySessionId);
            Assert.Equal(sessions[0].Id, sessions[1].ParentSessionId);
            Assert.Equal(sessions[1].Id, sessions[2].ParentSessionId);

            var rawTokens = new[] { first.RefreshToken, second.RefreshToken, third.RefreshToken };
            Assert.All(sessions, session => Assert.DoesNotContain(session.RefreshTokenHash.Value, rawTokens));
        }

        var independent = await AuthenticateAsync(factory, client, email);
        using var logout = await PostLogoutAsync(client, independent.AccessToken);
        using var repeatedLogout = await PostLogoutAsync(client, independent.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeatedLogout.StatusCode);
        Assert.Equal(string.Empty, await logout.Content.ReadAsStringAsync());
        using var afterLogout = await client.PostAsJsonAsync(
            RefreshEndpoint,
            new { refreshToken = independent.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);

        var logs = string.Join(Environment.NewLine, loggerProvider.Messages);
        foreach (var token in new[]
                 {
                     first.AccessToken,
                     first.RefreshToken,
                     second.AccessToken,
                     second.RefreshToken,
                     third.AccessToken,
                     third.RefreshToken,
                     independent.AccessToken,
                     independent.RefreshToken
                 })
        {
            Assert.DoesNotContain(token, logs, StringComparison.Ordinal);
        }

        Assert.Contains("reuse detected", logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentRefresh_CreatesNoActiveBranchAfterDuplicateUseIsDetected()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(
            factory,
            client,
            $"refresh-race-{UniqueSuffix()}@example.com");

        var requests = Enumerable.Range(0, 2)
            .Select(_ => client.PostAsJsonAsync(
                RefreshEndpoint,
                new { refreshToken = authentication.RefreshToken }))
            .ToArray();
        var responses = await Task.WhenAll(requests);
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var dbContext = CreateDbContext();
        var accountId = EntityId.From(authentication.Account.AccountId);
        var sessions = await dbContext.RefreshSessions
            .AsNoTracking()
            .Where(session => session.AccountId == accountId)
            .ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => Assert.Equal(RefreshSessionStatus.Revoked, session.Status));
        Assert.Empty(sessions.Where(session => session.Status == RefreshSessionStatus.Active));
    }

    [Fact]
    public async Task ExpiredMalformedRevokedAndDisabledRefreshTokens_ReturnGenericUnauthorized()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using (var malformed = await client.PostAsJsonAsync(
                   RefreshEndpoint,
                   new { refreshToken = "not-a-refresh-token" }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, malformed.StatusCode);
        }

        var expired = await AuthenticateAsync(
            factory,
            client,
            $"expired-refresh-{UniqueSuffix()}@example.com");
        await using (var dbContext = CreateDbContext())
        {
            var accountId = EntityId.From(expired.Account.AccountId);
            var past = DateTimeOffset.UtcNow.AddDays(-1);
            await dbContext.RefreshSessions
                .Where(session => session.AccountId == accountId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(session => session.CreatedAt, past.AddDays(-1))
                    .SetProperty(session => session.ExpiresAt, past));
        }

        using (var response = await client.PostAsJsonAsync(
                   RefreshEndpoint,
                   new { refreshToken = expired.RefreshToken }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var disabled = await AuthenticateAsync(
            factory,
            client,
            $"disabled-refresh-{UniqueSuffix()}@example.com");
        await using (var dbContext = CreateDbContext())
        {
            var account = await dbContext.Accounts.SingleAsync(
                candidate => candidate.Id == EntityId.From(disabled.Account.AccountId));
            account.Disable(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        using (var response = await client.PostAsJsonAsync(
                   RefreshEndpoint,
                   new { refreshToken = disabled.RefreshToken }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var invalidBearer = await PostLogoutAsync(client, "not-a-jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, invalidBearer.StatusCode);
    }

    private async Task<AuthenticationResponse> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client,
        string email)
    {
        var sender = factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>();
        var messageCount = sender.Messages.Count;
        using var challenge = await client.PostAsJsonAsync(ChallengeEndpoint, new { email });
        Assert.Equal(HttpStatusCode.Accepted, challenge.StatusCode);
        Assert.Equal(messageCount + 1, sender.Messages.Count);

        var code = sender.Messages.Last().OneTimeCode;
        using var verification = await client.PostAsJsonAsync(
            VerifyEndpoint,
            new { email, code });
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        return (await verification.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
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

    private static string CreateJwt(
        string issuer,
        string audience,
        string signingKey,
        Guid accountId,
        Guid sessionId,
        DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString("D")),
                new Claim("sid", sessionId.ToString("D"))
            ],
            now.UtcDateTime,
            expiresAt.UtcDateTime,
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
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
        AccountResponse Account);

    private sealed record AccountResponse(Guid AccountId, Guid ProfileId, string BeeexyId);
}
