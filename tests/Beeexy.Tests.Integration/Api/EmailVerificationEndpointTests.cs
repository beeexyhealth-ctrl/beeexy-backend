using System.Net;
using System.Net.Http.Json;
using System.Text;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class EmailVerificationEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string ChallengeEndpoint = "/api/v1/auth/email/challenges";
    private const string VerifyEndpoint = "/api/v1/auth/email/verify";

    [Fact]
    public async Task RequestThenVerify_NewAccount_AtomicallyProvisionsIdentityAndPreventsReplay()
    {
        await EnsureMigratedAsync();
        var email = $"new-{UniqueSuffix()}@example.com";
        using var loggerProvider = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: loggerProvider);
        using var client = factory.CreateApiClient();

        using var requested = await client.PostAsJsonAsync(ChallengeEndpoint, new { email });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var message = Assert.Single(
            factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>().Messages);

        using var verified = await client.PostAsJsonAsync(
            VerifyEndpoint,
            new { email = $"  {email.ToUpperInvariant()}  ", code = message.OneTimeCode });
        var identity = await verified.Content.ReadFromJsonAsync<AuthenticationResponse>();

        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.NotNull(identity);
        Assert.NotEmpty(identity.AccessToken);
        Assert.NotEmpty(identity.RefreshToken);
        Assert.True(identity.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
        Assert.True(identity.RefreshTokenExpiresAt > identity.AccessTokenExpiresAt);
        Assert.NotEqual(Guid.Empty, identity.Account.AccountId);
        Assert.NotEqual(Guid.Empty, identity.Account.ProfileId);
        Assert.StartsWith("BXY-", identity.Account.BeeexyId, StringComparison.Ordinal);

        await using (var dbContext = CreateDbContext())
        {
            var normalizedEmail = NormalizedEmail.Create(email);
            var account = await dbContext.Accounts
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Email == normalizedEmail);
            var profile = await dbContext.PatientProfiles
                .AsNoTracking()
                .SingleAsync(candidate => candidate.AccountId == account.Id);
            var preference = await dbContext.UserPreferences
                .AsNoTracking()
                .SingleAsync(candidate => candidate.AccountId == account.Id);
            var challenge = await dbContext.EmailAuthenticationChallenges
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Email == normalizedEmail);
            var session = await dbContext.RefreshSessions
                .AsNoTracking()
                .SingleAsync(candidate => candidate.AccountId == account.Id);

            Assert.Equal(identity.Account.AccountId, account.Id.Value);
            Assert.Equal(identity.Account.ProfileId, profile.Id.Value);
            Assert.Equal(identity.Account.BeeexyId, profile.BeeexyId.Value);
            Assert.Equal("Etc/UTC", preference.TimeZone.Value);
            Assert.Equal(ChallengeStatus.Consumed, challenge.Status);
            Assert.NotNull(challenge.ConsumedAt);
            Assert.Equal(challenge.ConsumedAt, challenge.UpdatedAt);
            var refreshTokenService = factory.Services.GetRequiredService<IRefreshTokenService>();
            Assert.Equal(refreshTokenService.Hash(identity.RefreshToken), session.RefreshTokenHash);
            Assert.NotEqual(identity.RefreshToken, session.RefreshTokenHash.Value);
            Assert.Equal(session.Id, session.FamilyId);
            Assert.Null(session.ParentSessionId);
            Assert.Equal(RefreshSessionStatus.Active, session.Status);
        }

        using var replay = await client.PostAsJsonAsync(
            VerifyEndpoint,
            new { email, code = message.OneTimeCode });
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

        var logs = string.Join(Environment.NewLine, loggerProvider.Messages);
        Assert.DoesNotContain(message.OneTimeCode, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(identity.AccessToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(identity.RefreshToken, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidReturningAccount_IsReusedWithoutDuplicateProfileOrPreference()
    {
        await EnsureMigratedAsync();
        var email = $"returning-{UniqueSuffix()}@example.com";
        var existing = await SeedIdentityAsync(email, disabled: false);
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var code = await RequestCodeAsync(factory, client, email);

        using var response = await client.PostAsJsonAsync(VerifyEndpoint, new { email, code });
        var identity = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(identity);
        Assert.Equal(existing.AccountId, identity.Account.AccountId);
        Assert.Equal(existing.ProfileId, identity.Account.ProfileId);

        await using var dbContext = CreateDbContext();
        var normalized = NormalizedEmail.Create(email);
        Assert.Equal(1, await dbContext.Accounts.CountAsync(account => account.Email == normalized));
        Assert.Equal(1, await dbContext.PatientProfiles.CountAsync(profile => profile.AccountId == EntityId.From(existing.AccountId)));
        Assert.Equal(1, await dbContext.UserPreferences.CountAsync(preference => preference.AccountId == EntityId.From(existing.AccountId)));
    }

    [Fact]
    public async Task InvalidCode_IncrementsAttemptWithoutProvisioning()
    {
        await EnsureMigratedAsync();
        var email = $"invalid-{UniqueSuffix()}@example.com";
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var actualCode = await RequestCodeAsync(factory, client, email);
        var wrongCode = actualCode == "000000" ? "111111" : "000000";

        using var response = await client.PostAsJsonAsync(
            VerifyEndpoint,
            new { email, code = wrongCode });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var dbContext = CreateDbContext();
        var normalized = NormalizedEmail.Create(email);
        var challenge = await dbContext.EmailAuthenticationChallenges
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Email == normalized);
        Assert.Equal(1, challenge.AttemptCount);
        Assert.Equal(ChallengeStatus.Pending, challenge.Status);
        Assert.False(await dbContext.Accounts.AnyAsync(account => account.Email == normalized));
        Assert.False(await dbContext.PatientProfiles.AnyAsync(profile => profile.AccountId != null &&
            dbContext.Accounts.Any(account => account.Id == profile.AccountId && account.Email == normalized)));
    }

    [Fact]
    public async Task ExpiredChallenge_ReturnsUnauthorizedPersistsExpirationAndCreatesNothing()
    {
        await EnsureMigratedAsync();
        var email = $"expired-{UniqueSuffix()}@example.com";
        const string code = "583104";
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var hasher = factory.Services.GetRequiredService<IOneTimePasswordHasher>();
        var id = EntityId.New();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-20);
        var challenge = EmailAuthenticationChallenge.Create(
            NormalizedEmail.Create(email),
            hasher.Hash(id, code),
            createdAt.AddMinutes(10),
            createdAt,
            id);
        await SaveAsync(challenge);

        using var response = await client.PostAsJsonAsync(VerifyEndpoint, new { email, code });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var dbContext = CreateDbContext();
        var saved = await dbContext.EmailAuthenticationChallenges
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == id);
        Assert.Equal(ChallengeStatus.Expired, saved.Status);
        Assert.False(await dbContext.Accounts.AnyAsync(account => account.Email == NormalizedEmail.Create(email)));
    }

    [Fact]
    public async Task ExhaustedAttempts_Returns429AndCorrectCodeCannotBypassLimit()
    {
        await EnsureMigratedAsync();
        var email = $"attempts-{UniqueSuffix()}@example.com";
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Authentication:EmailChallenge:MaximumVerificationAttempts"] = "2"
            });
        using var client = factory.CreateApiClient();
        var actualCode = await RequestCodeAsync(factory, client, email);
        var wrongCode = actualCode == "000000" ? "111111" : "000000";

        using var first = await client.PostAsJsonAsync(VerifyEndpoint, new { email, code = wrongCode });
        using var second = await client.PostAsJsonAsync(VerifyEndpoint, new { email, code = wrongCode });
        using var exhausted = await client.PostAsJsonAsync(VerifyEndpoint, new { email, code = actualCode });

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, exhausted.StatusCode);
        await using var dbContext = CreateDbContext();
        var normalized = NormalizedEmail.Create(email);
        Assert.Equal(2, (await dbContext.EmailAuthenticationChallenges
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Email == normalized)).AttemptCount);
        Assert.False(await dbContext.Accounts.AnyAsync(account => account.Email == normalized));
    }

    [Fact]
    public async Task DisabledAccount_ReturnsGenericUnauthorizedAndConsumesValidChallenge()
    {
        await EnsureMigratedAsync();
        var email = $"disabled-{UniqueSuffix()}@example.com";
        await SeedIdentityAsync(email, disabled: true);
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var code = await RequestCodeAsync(factory, client, email);

        using var response = await client.PostAsJsonAsync(VerifyEndpoint, new { email, code });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("disabled", body, StringComparison.OrdinalIgnoreCase);
        await using var dbContext = CreateDbContext();
        Assert.Equal(
            ChallengeStatus.Consumed,
            (await dbContext.EmailAuthenticationChallenges
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Email == NormalizedEmail.Create(email))).Status);
        var disabledAccount = await dbContext.Accounts
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Email == NormalizedEmail.Create(email));
        Assert.False(await dbContext.RefreshSessions.AnyAsync(
            session => session.AccountId == disabledAccount.Id));
    }

    [Fact]
    public async Task ReturningAccountWithoutPrimaryProfile_FailsSafelyWithoutRepairOrConsumption()
    {
        await EnsureMigratedAsync();
        var email = $"inconsistent-{UniqueSuffix()}@example.com";
        await SaveAsync(Account.Create(NormalizedEmail.Create(email), DateTimeOffset.UtcNow));
        using var loggerProvider = new InMemoryLoggerProvider();
        using var factory = new BeeexyApiFactory(
            postgres.ConnectionString,
            loggerProvider: loggerProvider);
        using var client = factory.CreateApiClient();
        var code = await RequestCodeAsync(factory, client, email);

        using var response = await client.PostAsJsonAsync(VerifyEndpoint, new { email, code });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("profile", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            loggerProvider.Messages,
            message => message.Contains(
                nameof(IdentityProvisioningInvariantException),
                StringComparison.Ordinal));
        await using var dbContext = CreateDbContext();
        Assert.False(await dbContext.PatientProfiles.AnyAsync(
            profile => profile.AccountId != null &&
                dbContext.Accounts.Any(account =>
                    account.Id == profile.AccountId &&
                    account.Email == NormalizedEmail.Create(email))));
        Assert.Equal(
            ChallengeStatus.Pending,
            (await dbContext.EmailAuthenticationChallenges
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Email == NormalizedEmail.Create(email))).Status);
    }

    [Fact]
    public async Task UnknownChallenge_ReturnsGenericUnauthorizedAndCreatesNothing()
    {
        await EnsureMigratedAsync();
        var email = $"unknown-{UniqueSuffix()}@example.com";
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            VerifyEndpoint,
            new { email, code = "123456", beeexyId = "BXY-NO-AUTHORITY" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("BXY-NO-AUTHORITY", body, StringComparison.Ordinal);
        await using var dbContext = CreateDbContext();
        Assert.False(await dbContext.Accounts.AnyAsync(
            account => account.Email == NormalizedEmail.Create(email)));
    }

    [Theory]
    [InlineData(null, "123456")]
    [InlineData("person@example.com", null)]
    [InlineData("person@example.com", "12345")]
    [InlineData("person@example.com", "abcdef")]
    public async Task InvalidFields_ReturnUnprocessableEntity(string? email, string? code)
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(VerifyEndpoint, new { email, code });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task MalformedJson_ReturnsSafeBadRequest()
    {
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        using var content = new StringContent("{\"email\":", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(VerifyEndpoint, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(
            "JsonException",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentVerification_ConsumesOnceAndProvisionsOneIdentity()
    {
        await EnsureMigratedAsync();
        var email = $"concurrent-verify-{UniqueSuffix()}@example.com";
        using var factory = new BeeexyApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();
        var code = await RequestCodeAsync(factory, client, email);

        var requests = Enumerable.Range(0, 2)
            .Select(_ => client.PostAsJsonAsync(VerifyEndpoint, new { email, code }))
            .ToArray();
        var responses = await Task.WhenAll(requests);
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

        await using var dbContext = CreateDbContext();
        var normalized = NormalizedEmail.Create(email);
        var account = await dbContext.Accounts.SingleAsync(candidate => candidate.Email == normalized);
        Assert.Equal(1, await dbContext.PatientProfiles.CountAsync(profile => profile.AccountId == account.Id));
        Assert.Equal(1, await dbContext.UserPreferences.CountAsync(preference => preference.AccountId == account.Id));
        var challenge = await dbContext.EmailAuthenticationChallenges.SingleAsync(candidate => candidate.Email == normalized);
        Assert.Equal(ChallengeStatus.Consumed, challenge.Status);
        Assert.NotNull(challenge.ConsumedAt);
    }

    private async Task<string> RequestCodeAsync(
        BeeexyApiFactory factory,
        HttpClient client,
        string email)
    {
        var sender = factory.Services.GetRequiredService<InMemoryAuthenticationEmailSender>();
        var countBefore = sender.Messages.Count;
        using var response = await client.PostAsJsonAsync(ChallengeEndpoint, new { email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(countBefore + 1, sender.Messages.Count);
        return sender.Messages.Last().OneTimeCode;
    }

    private async Task<(Guid AccountId, Guid ProfileId)> SeedIdentityAsync(
        string email,
        bool disabled)
    {
        var now = DateTimeOffset.UtcNow;
        var account = Account.Create(NormalizedEmail.Create(email), now);
        if (disabled)
        {
            account.Disable(now);
        }

        var profile = PatientProfile.Create(
            BeeexyId.Create($"BXY-{Guid.NewGuid():N}"),
            now,
            account.Id);
        var preference = UserPreference.Create(
            account.Id,
            UserTimeZone.Create("Etc/UTC"),
            now);
        await SaveAsync(account, profile, preference);
        return (account.Id.Value, profile.Id.Value);
    }

    private async Task SaveAsync(params object[] entities)
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(entities);
        await dbContext.SaveChangesAsync();
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

    private static string UniqueSuffix() => Guid.NewGuid().ToString("N");

    private sealed record AuthenticationResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt,
        DateTimeOffset RefreshTokenExpiresAt,
        AccountResponse Account);

    private sealed record AccountResponse(Guid AccountId, Guid ProfileId, string BeeexyId);
}
