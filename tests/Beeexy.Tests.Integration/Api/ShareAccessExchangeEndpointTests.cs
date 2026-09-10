using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Beeexy.Api.Sharing;
using Beeexy.Application.Identity;
using Beeexy.Application.Sharing;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Sharing;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Infrastructure.Sharing;
using Beeexy.Tests.Integration.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class ShareAccessExchangeEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string ExchangeEndpoint = "/api/v1/shared-access/exchange";
    private static readonly DateTimeOffset Now = Normalize(DateTimeOffset.UtcNow);

    [Fact]
    [Trait("Category", "Phase113")]
    public async Task ActiveCapability_IsReusableAndShareTokenIsIsolatedFromAccountAuthority()
    {
        await EnsureMigratedAsync();
        var logs = new InMemoryLoggerProvider();
        var clock = new MutableClock(Now);
        using var factory = CreateFactory(clock, logs);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "success");
        var share = await CreateShareAsync(client, authentication.AccessToken, lifetimeMinutes: 60);
        client.DefaultRequestHeaders.Authorization = null;

        var exchangeResponses = await Task.WhenAll(
            client.PostAsJsonAsync(
                ExchangeEndpoint,
                new { capability = share.Capability }),
            client.PostAsJsonAsync(
                ExchangeEndpoint,
                new { capability = share.Capability }));
        using var first = exchangeResponses[0];
        using var second = exchangeResponses[1];
        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        using var firstDocument = JsonDocument.Parse(firstBody);
        using var secondDocument = JsonDocument.Parse(secondBody);
        var firstToken = Assert.IsType<string>(
            firstDocument.RootElement.GetProperty("accessToken").GetString());
        var secondToken = Assert.IsType<string>(
            secondDocument.RootElement.GetProperty("accessToken").GetString());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal("Bearer", firstDocument.RootElement.GetProperty("tokenType").GetString());
        Assert.Equal(
            Now.AddMinutes(15),
            firstDocument.RootElement.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(["accessToken", "expiresAt", "tokenType"],
            firstDocument.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(value => value, StringComparer.Ordinal));
        foreach (var forbidden in new[]
                 {
                     "patient", "clinical", "history", "triage", "account", "beeexy",
                     "capability", "hash", "creator", "shareUrl"
                 })
        {
            Assert.DoesNotContain(forbidden, firstBody, StringComparison.OrdinalIgnoreCase);
        }

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(firstToken);
        Assert.Equal(["beeexy-share-access"], jwt.Audiences);
        Assert.Equal(
            ShareAccessTokenClaims.CredentialTypeValue,
            jwt.Claims.Single(claim =>
                claim.Type == ShareAccessTokenClaims.CredentialType).Value);
        Assert.Equal(
            share.ShareGrantId.ToString("D"),
            jwt.Claims.Single(claim =>
                claim.Type == ShareAccessTokenClaims.ShareGrantId).Value);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type is "sub" or "sid");

        Assert.True(await AuthenticateSchemeAsync(
            factory,
            firstToken,
            ShareAccessAuthenticationDefaults.Scheme));
        Assert.False(await AuthenticateSchemeAsync(
            factory,
            authentication.AccessToken,
            ShareAccessAuthenticationDefaults.Scheme));

        SetBearer(client, firstToken);
        using var accountRoute = await client.GetAsync("/api/v1/shares");
        using var accountMutation = await client.PostAsJsonAsync(
            "/api/v1/shares",
            new { scope = "FullProfile", idempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, accountRoute.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, accountMutation.StatusCode);

        await using (var dbContext = CreateDbContext())
        {
            var grantId = EntityId.From(share.ShareGrantId);
            Assert.Equal(1, await dbContext.ShareGrants.CountAsync(value => value.Id == grantId));
            var events = await dbContext.ShareAccessEvents
                .Where(value => value.ShareGrantId == grantId)
                .ToArrayAsync();
            Assert.Single(events);
            Assert.Equal(ShareAccessEventType.ShareCreated, events[0].EventType);
        }

        var logText = string.Join('\n', logs.Messages);
        Assert.DoesNotContain(share.Capability, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(firstToken, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(secondToken, logText, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256:", logText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Phase113")]
    public async Task GrantExpiryBoundsTokenAndExactBoundaryIsDenied()
    {
        await EnsureMigratedAsync();
        var clock = new MutableClock(Now);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "expiry");
        var share = await CreateShareAsync(client, authentication.AccessToken, lifetimeMinutes: 1);
        client.DefaultRequestHeaders.Authorization = null;

        using var success = await client.PostAsJsonAsync(
            ExchangeEndpoint,
            new { capability = share.Capability });
        using var successDocument = JsonDocument.Parse(await success.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal(
            Now.AddMinutes(1),
            successDocument.RootElement.GetProperty("expiresAt").GetDateTimeOffset());

        clock.UtcNow = Now.AddMinutes(1);
        using var expired = await client.PostAsJsonAsync(
            ExchangeEndpoint,
            new { capability = share.Capability });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    [Trait("Category", "Phase113")]
    public async Task MissingMalformedWrongRevokedAndUnsupportedCredentialsShareOneSafeError()
    {
        await EnsureMigratedAsync();
        var clock = new MutableClock(Now);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "failure");
        var revokedShare = await CreateShareAsync(client, authentication.AccessToken);
        var capabilityService = new CryptographicShareCapabilityService();
        var reservedCapability = capabilityService.Generate();
        var unsupportedCapability = capabilityService.Generate();
        await using (var dbContext = CreateDbContext())
        {
            var revoked = await dbContext.ShareGrants.SingleAsync(
                value => value.Id == EntityId.From(revokedShare.ShareGrantId));
            revoked.Revoke(EntityId.From(authentication.Account.AccountId), Now);
            dbContext.ShareGrants.Add(CreatePersistedGrant(
                authentication,
                reservedCapability.Hash,
                ShareScope.Case));
            dbContext.ShareGrants.Add(CreatePersistedGrant(
                authentication,
                unsupportedCapability.Hash,
                ShareScope.SpecificRecords));
            await dbContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization = null;
        var attempts = new object[]
        {
            new { capability = (string?)null },
            new { capability = string.Empty },
            new { capability = "malformed" },
            new { capability = "shc1." + new string('A', 43) },
            new { capability = revokedShare.Capability },
            new { capability = reservedCapability.Value },
            new { capability = unsupportedCapability.Value },
            new { capability = revokedShare.ShareGrantId.ToString("D") },
            new { capability = authentication.Account.ProfileId.ToString("D") },
            new { capability = authentication.Account.BeeexyId }
        };
        string? safeTitle = null;
        string? safeDetail = null;
        foreach (var attempt in attempts)
        {
            using var response = await client.PostAsJsonAsync(ExchangeEndpoint, attempt);
            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            safeTitle ??= document.RootElement.GetProperty("title").GetString();
            safeDetail ??= document.RootElement.GetProperty("detail").GetString();
            Assert.Equal(safeTitle, document.RootElement.GetProperty("title").GetString());
            Assert.Equal(safeDetail, document.RootElement.GetProperty("detail").GetString());
            Assert.DoesNotContain("sha256:", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("revoked", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("expired", body, StringComparison.OrdinalIgnoreCase);
        }

        SetBearer(client, authentication.AccessToken);
        using var accountBearerWithoutCapability = await client.PostAsJsonAsync(
            ExchangeEndpoint,
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, accountBearerWithoutCapability.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        using var queryOnly = await client.PostAsJsonAsync(
            $"{ExchangeEndpoint}?capability={Uri.EscapeDataString(revokedShare.Capability)}",
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, queryOnly.StatusCode);

        using var identifierField = await client.PostAsJsonAsync(
            ExchangeEndpoint,
            new { shareGrantId = revokedShare.ShareGrantId });
        Assert.Equal(HttpStatusCode.Unauthorized, identifierField.StatusCode);

        using var malformedJson = await client.PostAsync(
            ExchangeEndpoint,
            new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformedJson.StatusCode);
    }

    [Fact]
    [Trait("Category", "Phase113")]
    public async Task FixedWindowLimiter_ThrottlesSafelyAndRecoversWithoutSecretLeakage()
    {
        await EnsureMigratedAsync();
        var logs = new InMemoryLoggerProvider();
        var clock = new MutableClock(Now);
        using var factory = CreateFactory(
            clock,
            logs,
            permitLimit: 2,
            windowSeconds: 1);
        using var client = factory.CreateApiClient();
        var authentication = await AuthenticateAsync(factory, client, "rate-limit");
        var share = await CreateShareAsync(client, authentication.AccessToken);
        client.DefaultRequestHeaders.Authorization = null;

        using var first = await client.PostAsJsonAsync(
            ExchangeEndpoint,
            new { capability = share.Capability });
        using var second = await client.PostAsJsonAsync(
            ExchangeEndpoint,
            new { capability = "shc1." + new string('Y', 43) });
        using var throttled = await client.PostAsJsonAsync(
            ExchangeEndpoint,
            new { capability = "shc1." + new string('X', 43) });
        var throttledBody = await throttled.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Contains("try again later", throttledBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(share.Capability, throttledBody, StringComparison.Ordinal);
        Assert.DoesNotContain("hash", throttledBody, StringComparison.OrdinalIgnoreCase);

        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        using var recovered = await client.PostAsJsonAsync(
            ExchangeEndpoint,
            new { capability = "shc1." + new string('Z', 43) });
        Assert.Equal(HttpStatusCode.Unauthorized, recovered.StatusCode);

        var logText = string.Join('\n', logs.Messages);
        Assert.DoesNotContain(share.Capability, logText, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256:", logText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Phase113")]
    public async Task OpenApi_AddsExactlyOnePublicExchangeOperationAndNoLaterRoutes()
    {
        await EnsureMigratedAsync();
        using var factory = CreateFactory(new MutableClock(Now));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(58, paths.EnumerateObject().Count());
        var exchangePath = paths.GetProperty(ExchangeEndpoint);
        var operation = exchangePath.GetProperty("post");
        Assert.Single(exchangePath.EnumerateObject().Where(value => value.Name == "post"));
        Assert.False(operation.TryGetProperty("security", out _));
        Assert.Equal(
            ["200", "400", "401", "429", "500"],
            operation.GetProperty("responses").EnumerateObject()
                .Select(value => value.Name)
                .OrderBy(value => value, StringComparer.Ordinal));
        var requestSchema = document.RootElement.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ExchangeShareCapabilityRequest");
        Assert.Equal(
            ["capability"],
            requestSchema.GetProperty("properties").EnumerateObject()
                .Select(value => value.Name));
        var responseSchema = document.RootElement.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ExchangeShareCapabilityResponse");
        Assert.Equal(
            ["accessToken", "tokenType", "expiresAt"],
            responseSchema.GetProperty("properties").EnumerateObject()
                .Select(value => value.Name));
        Assert.True(document.RootElement.GetProperty("components")
            .GetProperty("securitySchemes")
            .TryGetProperty(ShareAccessAuthenticationDefaults.Scheme, out var shareScheme));
        Assert.Equal("bearer", shareScheme.GetProperty("scheme").GetString());
        Assert.DoesNotContain(paths.EnumerateObject(), path =>
            path.Name.StartsWith("/api/v1/exports", StringComparison.Ordinal));
    }

    private BeeexyApiFactory CreateFactory(
        MutableClock clock,
        InMemoryLoggerProvider? logger = null,
        int permitLimit = 100,
        int windowSeconds = 60) => new(
        postgres.ConnectionString,
        loggerProvider: logger,
        configurationOverrides: new Dictionary<string, string?>
        {
            ["Sharing:ExchangeRateLimit:PermitLimit"] = permitLimit.ToString(),
            ["Sharing:ExchangeRateLimit:WindowSeconds"] = windowSeconds.ToString()
        },
        configureServices: services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
        });

    private async Task<AuthenticationResult> AuthenticateAsync(
        BeeexyApiFactory factory,
        HttpClient client,
        string prefix)
    {
        var email = $"share-exchange-{prefix}-{Guid.NewGuid():N}@example.com";
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

    private static async Task<bool> AuthenticateSchemeAsync(
        BeeexyApiFactory factory,
        string token,
        string scheme)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        context.Request.Headers.Authorization = $"Bearer {token}";
        var result = await context.AuthenticateAsync(scheme);
        return result.Succeeded;
    }

    private static ShareGrant CreatePersistedGrant(
        AuthenticationResult authentication,
        TokenHash capabilityHash,
        ShareScope scope) => ShareGrant.Create(
        EntityId.From(authentication.Account.ProfileId),
        EntityId.From(authentication.Account.AccountId),
        EntityId.New(),
        ShareRequestFingerprint.Create(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()))
            .ToLowerInvariant()),
        scope,
        capabilityHash,
        Now.AddMinutes(-1),
        Now.AddHours(1));

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed record AuthenticationResult(
        string AccessToken,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);

    private sealed record CreatedShare(Guid ShareGrantId, string Capability);
}
