using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Identity;
using Beeexy.Domain.Identity;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class EmailChallengeEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string Endpoint = "/api/v1/auth/email/challenges";

    [Fact]
    public async Task ValidAnonymousRequest_ReturnsSafeAcceptedAndPersistsOnlyHash()
    {
        await EnsureMigratedAsync();
        var email = $"anonymous-{UniqueSuffix()}@example.com";
        using var loggerProvider = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: loggerProvider);
        using var client = factory.CreateApiClient();
        var profileCountBefore = await CountProfilesAsync();

        using var response = await client.PostAsJsonAsync(Endpoint, new { email });
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(string.Empty, responseBody);
        Assert.False(response.Headers.Contains("WWW-Authenticate"));

        var sender = factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>();
        var message = Assert.Single(sender.Messages);
        Assert.Equal(email, message.Recipient.Value);
        Assert.Matches("^[0-9]{6}$", message.OneTimeCode);

        await using var dbContext = CreateDbContext();
        var challenge = await dbContext.EmailAuthenticationChallenges
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Email == NormalizedEmail.Create(email));
        Assert.Equal(ChallengeStatus.Pending, challenge.Status);
        Assert.Equal(0, challenge.AttemptCount);
        Assert.Equal(TimeSpan.FromMinutes(10), challenge.ExpiresAt - challenge.CreatedAt);
        Assert.StartsWith("hmac-sha256:", challenge.OtpHash.Value, StringComparison.Ordinal);
        Assert.NotEqual(message.OneTimeCode, challenge.OtpHash.Value);
        Assert.False(await dbContext.Accounts.AnyAsync(account => account.Email == challenge.Email));
        Assert.Equal(profileCountBefore, await CountProfilesAsync());

        var logs = string.Join(Environment.NewLine, loggerProvider.Messages);
        Assert.DoesNotContain(message.OneTimeCode, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(challenge.OtpHash.Value, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(email, logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EquivalentEmailForms_ReplacePriorPendingChallenge()
    {
        await EnsureMigratedAsync();
        var localPart = $"replace-{UniqueSuffix()}";
        var normalized = $"{localPart}@example.com";
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var first = await client.PostAsJsonAsync(
            Endpoint,
            new { email = $"  {localPart.ToUpperInvariant()}@Example.COM  " });
        using var second = await client.PostAsJsonAsync(Endpoint, new { email = normalized });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var sender = factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>();
        Assert.Equal(2, sender.Messages.Count);

        await using var dbContext = CreateDbContext();
        var challenges = await dbContext.EmailAuthenticationChallenges
            .AsNoTracking()
            .Where(challenge => challenge.Email == NormalizedEmail.Create(normalized))
            .ToListAsync();
        var current = Assert.Single(challenges);
        Assert.Equal(ChallengeStatus.Pending, current.Status);
        var hasher = factory.Services.GetRequiredService<IOneTimePasswordHasher>();
        Assert.Equal(
            current.OtpHash,
            hasher.Hash(current.Id, sender.Messages.Last().OneTimeCode));
    }

    [Fact]
    public async Task InvalidEmail_ReturnsSafeUnprocessableEntity()
    {
        await EnsureMigratedAsync();
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(Endpoint, new { email = "not-an-email" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("authentication.invalid_email", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>().Messages);
    }

    [Fact]
    public async Task MalformedJson_ReturnsBadRequestProblemDetails()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        using var content = new StringContent("{\"email\":", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(Endpoint, content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("JsonException", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalizedEmailLimit_Returns429WithRetryAfter()
    {
        await EnsureMigratedAsync();
        var localPart = $"email-limit-{UniqueSuffix()}";
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: RateLimits(emailLimit: 2, ipLimit: 20));
        using var client = factory.CreateApiClient();

        using var first = await client.PostAsJsonAsync(
            Endpoint,
            new { email = $"{localPart}@example.com" });
        using var second = await client.PostAsJsonAsync(
            Endpoint,
            new { email = $" {localPart.ToUpperInvariant()}@Example.COM " });
        using var third = await client.PostAsJsonAsync(
            Endpoint,
            new { email = $"{localPart}@example.com" });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task RequesterIpLimit_Returns429AcrossDifferentEmails()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: RateLimits(emailLimit: 20, ipLimit: 2));
        using var client = factory.CreateApiClient();

        using var first = await client.PostAsJsonAsync(Endpoint, new { email = $"ip-a-{suffix}@example.com" });
        using var second = await client.PostAsJsonAsync(Endpoint, new { email = $"ip-b-{suffix}@example.com" });
        using var third = await client.PostAsJsonAsync(Endpoint, new { email = $"ip-c-{suffix}@example.com" });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task RegisteredAndUnregisteredEmails_HaveEquivalentPublicResponses()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var registeredEmail = $"registered-{suffix}@example.com";
        var unregisteredEmail = $"unregistered-{suffix}@example.com";
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Accounts.Add(Account.Create(
                NormalizedEmail.Create(registeredEmail),
                DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: RateLimits(emailLimit: 5, ipLimit: 10));
        using var client = factory.CreateApiClient();

        using var registered = await client.PostAsJsonAsync(Endpoint, new { email = registeredEmail });
        using var unregistered = await client.PostAsJsonAsync(Endpoint, new { email = unregisteredEmail });

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(registered.StatusCode, unregistered.StatusCode);
        Assert.Equal(
            await registered.Content.ReadAsStringAsync(),
            await unregistered.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Throttling_DoesNotRevealRegistrationState()
    {
        await EnsureMigratedAsync();
        var suffix = UniqueSuffix();
        var registeredEmail = $"throttled-registered-{suffix}@example.com";
        var unregisteredEmail = $"throttled-unregistered-{suffix}@example.com";
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Accounts.Add(Account.Create(
                NormalizedEmail.Create(registeredEmail),
                DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: RateLimits(emailLimit: 1, ipLimit: 10));
        using var client = factory.CreateApiClient();

        await client.PostAsJsonAsync(Endpoint, new { email = registeredEmail });
        using var registeredThrottle = await client.PostAsJsonAsync(Endpoint, new { email = registeredEmail });
        await client.PostAsJsonAsync(Endpoint, new { email = unregisteredEmail });
        using var unregisteredThrottle = await client.PostAsJsonAsync(Endpoint, new { email = unregisteredEmail });

        Assert.Equal(HttpStatusCode.TooManyRequests, registeredThrottle.StatusCode);
        Assert.Equal(registeredThrottle.StatusCode, unregisteredThrottle.StatusCode);
        Assert.Equal(
            await ProblemTitleAsync(registeredThrottle),
            await ProblemTitleAsync(unregisteredThrottle));
    }

    [Fact]
    public async Task BeeexyIdInput_HasNoAuthenticationAuthorityOrResponseExposure()
    {
        await EnsureMigratedAsync();
        var email = $"beeexy-id-ignored-{UniqueSuffix()}@example.com";
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { email, beeexyId = "BXY-MUST-NOT-AUTHENTICATE" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain("BXY-MUST-NOT-AUTHENTICATE", body, StringComparison.Ordinal);
        await using var dbContext = CreateDbContext();
        Assert.False(await dbContext.Accounts.AnyAsync(account => account.Email == NormalizedEmail.Create(email)));
    }

    [Fact]
    public async Task DeliveryFailure_ReturnsSafe500DeletesChallengeAndDoesNotLogOtp()
    {
        await EnsureMigratedAsync();
        var email = $"delivery-failure-{UniqueSuffix()}@example.com";
        var failingSender = new FailingAuthenticationEmailSender();
        using var loggerProvider = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: loggerProvider,
            configureServices: services =>
            {
                services.RemoveAll<IAuthenticationEmailSender>();
                services.AddSingleton<IAuthenticationEmailSender>(failingSender);
            });
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(Endpoint, new { email });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotNull(failingSender.OneTimeCode);
        Assert.DoesNotContain("provider", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(failingSender.OneTimeCode!, body, StringComparison.Ordinal);
        var logs = string.Join(Environment.NewLine, loggerProvider.Messages);
        Assert.DoesNotContain(failingSender.OneTimeCode!, logs, StringComparison.Ordinal);

        await using var dbContext = CreateDbContext();
        Assert.False(await dbContext.EmailAuthenticationChallenges
            .AnyAsync(challenge => challenge.Email == NormalizedEmail.Create(email)));
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

    private async Task<int> CountProfilesAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.PatientProfiles.CountAsync();
    }

    private static IReadOnlyDictionary<string, string?> RateLimits(
        int emailLimit,
        int ipLimit)
    {
        return new Dictionary<string, string?>
        {
            ["Authentication:EmailChallenge:EmailPermitLimit"] = emailLimit.ToString(),
            ["Authentication:EmailChallenge:IpPermitLimit"] = ipLimit.ToString()
        };
    }

    private static async Task<string?> ProblemTitleAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("title").GetString();
    }

    private static string UniqueSuffix()
    {
        return Guid.NewGuid().ToString("N");
    }

    private sealed class FailingAuthenticationEmailSender : IAuthenticationEmailSender
    {
        public string? OneTimeCode { get; private set; }

        public Task SendAsync(
            AuthenticationEmailMessage message,
            CancellationToken cancellationToken = default)
        {
            OneTimeCode = message.OneTimeCode;
            throw new InvalidOperationException(
                $"Provider failure containing secret {message.OneTimeCode}");
        }
    }
}
