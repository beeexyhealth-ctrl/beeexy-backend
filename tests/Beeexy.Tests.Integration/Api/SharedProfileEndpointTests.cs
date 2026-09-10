using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Identity;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Sharing;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class SharedProfileEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string ProfileEndpoint = "/api/v1/shared-access/profile";

    [Fact]
    [Trait("Category", "Phase114")]
    public async Task FullProfile_RevalidatesGrantReturnsAllowListAndRecordsIdempotentAccess()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "success");
        var share = await CreateShareAsync(client, authentication.AccessToken);
        var shareToken = await ExchangeAsync(client, share.Capability);

        SetBearer(client, shareToken);
        using var first = await client.GetAsync(ProfileEndpoint);
        using var second = await client.GetAsync(ProfileEndpoint);
        var body = await first.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("no-store", first.Headers.CacheControl?.ToString());
        Assert.Equal("FullProfile", document.RootElement.GetProperty("scope").GetString());
        var profile = document.RootElement.GetProperty("profile");
        Assert.Equal(
            ["clinicalHistory", "demographics", "preTriage", "secondOpinions",
                "symptomDiaryContent", "symptomDiaryEntries"],
            profile.EnumerateObject().Select(value => value.Name)
                .OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            authentication.Account.BeeexyId,
            profile.GetProperty("demographics").GetProperty("beeexyId").GetString());
        Assert.Empty(profile.GetProperty("clinicalHistory").EnumerateArray());
        Assert.Empty(profile.GetProperty("preTriage").EnumerateArray());
        Assert.Empty(profile.GetProperty("symptomDiaryEntries").EnumerateArray());
        Assert.Empty(profile.GetProperty("symptomDiaryContent").EnumerateArray());
        Assert.Empty(profile.GetProperty("secondOpinions").EnumerateArray());
        foreach (var forbidden in new[]
                 {
                     "accountId", "creator", "revoker", "capability", "token", "hash",
                     "conversation", "message", "prompt", "provider", "model", "storage",
                     "appointmentId", "clinicId", "doctorId", "notification", "preference",
                     "sourceReference", "importedAt", "audit", "telemetry"
                 })
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }

        using var overrideAttempt = await client.GetAsync(
            $"{ProfileEndpoint}?resourceId={Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.Unauthorized, overrideAttempt.StatusCode);

        await using var dbContext = CreateDbContext();
        var grantId = EntityId.From(share.ShareGrantId);
        var events = await dbContext.ShareAccessEvents.AsNoTracking()
            .Where(value => value.ShareGrantId == grantId)
            .OrderBy(value => value.OccurredAt)
            .ToArrayAsync();
        Assert.Equal(2, events.Length);
        Assert.Equal(ShareAccessEventType.ShareCreated, events[0].EventType);
        var accessed = events[1];
        Assert.Equal(ShareAccessEventType.ShareAccessed, accessed.EventType);
        Assert.Equal(ShareAccessOutcome.Succeeded, accessed.Outcome);
        Assert.Null(accessed.ResourceType);
    }

    [Fact]
    [Trait("Category", "Phase114")]
    public async Task MissingAccountBearerRevokedAndTokenScopeMismatch_FailSafely()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "denied");
        var share = await CreateShareAsync(client, authentication.AccessToken);

        client.DefaultRequestHeaders.Authorization = null;
        using var missing = await client.GetAsync(ProfileEndpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        SetBearer(client, authentication.AccessToken);
        using var accountBearer = await client.GetAsync(ProfileEndpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, accountBearer.StatusCode);

        var issuer = factory.Services.GetRequiredService<IShareAccessTokenIssuer>();
        var now = DateTimeOffset.UtcNow;
        var invalidTokens = new[]
        {
            "not-a-jwt",
            CreateToken(share.ShareGrantId, now, now.AddMinutes(5),
                signingKey: "wrong-signature-key-with-at-least-thirty-two-bytes"),
            CreateToken(share.ShareGrantId, now, now.AddMinutes(5), issuer: "wrong-issuer"),
            CreateToken(share.ShareGrantId, now, now.AddMinutes(5), audience: "wrong-audience"),
            CreateToken(share.ShareGrantId, now.AddMinutes(-10), now.AddMinutes(-5))
        };
        foreach (var invalidToken in invalidTokens)
        {
            SetBearer(client, invalidToken);
            using var invalid = await client.GetAsync(ProfileEndpoint);
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        }

        var missingGrantToken = issuer.Issue(
            EntityId.New(),
            ShareScope.FullProfile,
            now,
            now.AddMinutes(5)).Value;
        SetBearer(client, missingGrantToken);
        using var missingGrant = await client.GetAsync(ProfileEndpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, missingGrant.StatusCode);

        var mismatchedToken = issuer.Issue(
            EntityId.From(share.ShareGrantId),
            ShareScope.SpecificRecords,
            now,
            now.AddMinutes(5)).Value;
        SetBearer(client, mismatchedToken);
        using var mismatch = await client.GetAsync(ProfileEndpoint);
        var mismatchBody = await mismatch.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, mismatch.StatusCode);
        Assert.DoesNotContain("scope", mismatchBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(share.ShareGrantId.ToString("D"), mismatchBody, StringComparison.Ordinal);

        var shareToken = await ExchangeAsync(client, share.Capability);
        await using (var dbContext = CreateDbContext())
        {
            var grant = await dbContext.ShareGrants.SingleAsync(value =>
                value.Id == EntityId.From(share.ShareGrantId));
            grant.Revoke(EntityId.From(authentication.Account.AccountId), DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        SetBearer(client, shareToken);
        using var revoked = await client.GetAsync(ProfileEndpoint);
        var revokedBody = await revoked.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.DoesNotContain("revoked", revokedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(share.ShareGrantId.ToString("D"), revokedBody, StringComparison.Ordinal);

        await using var verify = CreateDbContext();
        Assert.DoesNotContain(
            await verify.ShareAccessEvents.AsNoTracking()
                .Where(value => value.ShareGrantId == EntityId.From(share.ShareGrantId))
                .ToArrayAsync(),
            value => value.EventType == ShareAccessEventType.ShareAccessed);
    }

    [Fact]
    [Trait("Category", "Phase114")]
    public async Task ShareAccessToken_CannotUseRepresentativeMutationRoutes()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "readonly");
        var share = await CreateShareAsync(client, authentication.AccessToken);
        var token = await ExchangeAsync(client, share.Capability);
        SetBearer(client, token);

        var attempts = new[]
        {
            client.PatchAsJsonAsync("/api/v1/patients/me", new { firstName = "No" }),
            client.PostAsJsonAsync("/api/v1/shares", new
            {
                scope = "FullProfile",
                idempotencyKey = Guid.NewGuid()
            }),
            client.PostAsJsonAsync(
                $"/api/v1/pre-triage/episodes/{Guid.NewGuid():D}/check-ins",
                new { }),
            client.PostAsJsonAsync("/api/v1/ai/second-opinions", new { text = "No" }),
            client.PostAsJsonAsync("/api/v1/appointments", new { })
        };
        var responses = await Task.WhenAll(attempts);
        try
        {
            Assert.All(responses, response =>
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    [Trait("Category", "Phase114")]
    public async Task GrantExactExpiryAfterTokenIssue_IsDeniedWithoutAccessEvent()
    {
        await EnsureMigratedAsync();
        var start = Normalize(DateTimeOffset.UtcNow);
        var clock = new MutableClock(start);
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
            });
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "grant-expiry");
        var share = await CreateShareAsync(client, authentication.AccessToken, 1);
        var token = await ExchangeAsync(client, share.Capability);

        clock.UtcNow = start.AddMinutes(1);
        SetBearer(client, token);
        using var response = await client.GetAsync(ProfileEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var dbContext = CreateDbContext();
        Assert.DoesNotContain(
            await dbContext.ShareAccessEvents.AsNoTracking()
                .Where(value => value.ShareGrantId == EntityId.From(share.ShareGrantId))
                .ToArrayAsync(),
            value => value.EventType == ShareAccessEventType.ShareAccessed);
    }

    [Fact]
    [Trait("Category", "Phase114")]
    public async Task OpenApi_AddsOnlyGetProfileWithDedicatedShareAccessSecurity()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(56, paths.EnumerateObject().Count());
        var profilePath = paths.GetProperty(ProfileEndpoint);
        Assert.True(profilePath.TryGetProperty("get", out var operation));
        Assert.False(profilePath.TryGetProperty("post", out _));
        var requirement = Assert.Single(operation.GetProperty("security").EnumerateArray());
        Assert.True(requirement.TryGetProperty("ShareAccess", out _));
        Assert.False(requirement.TryGetProperty("Bearer", out _));
        Assert.Equal(
            ["200", "401", "403", "404", "422", "500"],
            operation.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name)
                .OrderBy(value => value, StringComparer.Ordinal));
        var responseText = operation.GetProperty("responses").GetRawText();
        Assert.DoesNotContain("capability", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(paths.EnumerateObject(), path =>
            path.Name.Contains("revoke", StringComparison.OrdinalIgnoreCase) ||
            path.Name.Contains("activity", StringComparison.OrdinalIgnoreCase) ||
            path.Name.StartsWith("/api/v1/exports", StringComparison.Ordinal));
    }

    private BeeexyApiFactory CreateFactory() => new(postgres.ConnectionString);

    private async Task<AuthenticationResult> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client,
        string prefix)
    {
        var email = $"shared-profile-{prefix}-{Guid.NewGuid():N}@example.com";
        using var challenge = await client.PostAsJsonAsync(
            "/api/v1/auth/email/challenges",
            new { email });
        Assert.Equal(HttpStatusCode.Accepted, challenge.StatusCode);
        var message = Assert.Single(
            factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>().Messages,
            value => value.Recipient.Value == email);
        using var verification = await client.PostAsJsonAsync(
            "/api/v1/auth/email/verify",
            new { email, code = message.OneTimeCode });
        verification.EnsureSuccessStatusCode();
        return Assert.IsType<AuthenticationResult>(
            await verification.Content.ReadFromJsonAsync<AuthenticationResult>());
    }

    private static async Task<CreatedShare> CreateShareAsync(
        HttpClient client,
        string accountToken,
        int? lifetimeMinutes = null)
    {
        SetBearer(client, accountToken);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/shares",
            new
            {
                scope = "FullProfile",
                lifetimeMinutes,
                idempotencyKey = Guid.NewGuid()
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<CreatedShare>(await response.Content.ReadFromJsonAsync<CreatedShare>());
    }

    private static async Task<string> ExchangeAsync(HttpClient client, string capability)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.PostAsJsonAsync(
            "/api/v1/shared-access/exchange",
            new { capability });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Assert.IsType<string>(
            document.RootElement.GetProperty("accessToken").GetString());
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string CreateToken(
        Guid grantId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string issuer = "https://api.beeexy.com",
        string audience = "beeexy-share-access",
        string signingKey =
            "integration-test-only-jwt-signing-key-with-at-least-32-bytes")
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [
                new Claim(
                    ShareAccessTokenClaims.CredentialType,
                    ShareAccessTokenClaims.CredentialTypeValue),
                new Claim(ShareAccessTokenClaims.ShareGrantId, grantId.ToString("D")),
                new Claim(ShareAccessTokenClaims.ShareScope, ShareScope.FullProfile.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D"))
            ],
            issuedAt.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);
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

    private static DateTimeOffset Normalize(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(
            utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond),
            TimeSpan.Zero);
    }

    private sealed record CreatedShare(Guid ShareGrantId, string Capability);

    private sealed record AuthenticationResult(
        string AccessToken,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }
}
