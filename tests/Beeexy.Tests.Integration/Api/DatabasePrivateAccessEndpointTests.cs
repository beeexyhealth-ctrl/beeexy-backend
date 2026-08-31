using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Beeexy.Api.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Identity;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class DatabasePrivateAccessEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string Password = "DatabasePrivatePassword!123";
    private const string Keyword = "DatabasePrivateKeyword!456";

    [Fact]
    public async Task SeparateCredentials_IssueSeparateNormalIdentitiesAndEnforcePatientIsolation()
    {
        await EnsureMigratedAsync();
        var testerA = await AddTesterAsync("database-a");
        var testerB = await AddTesterAsync("database-b");
        using var factory = Factory();
        using var clientA = factory.CreateApiClient();
        using var clientB = factory.CreateApiClient();

        var authenticationA = await LoginAsync(clientA, testerA.Username);
        var authenticationB = await LoginAsync(clientB, testerB.Username);

        Assert.Equal(testerA.AccountId.Value, authenticationA.Account.AccountId);
        Assert.Equal(testerA.ProfileId.Value, authenticationA.Account.ProfileId);
        Assert.Equal(testerB.AccountId.Value, authenticationB.Account.AccountId);
        Assert.Equal(testerB.ProfileId.Value, authenticationB.Account.ProfileId);
        Assert.NotEqual(authenticationA.AccessToken, authenticationB.AccessToken);
        Assert.NotEqual(authenticationA.RefreshToken, authenticationB.RefreshToken);

        clientA.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authenticationA.AccessToken);
        clientB.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authenticationB.AccessToken);
        using var own = await clientA.GetAsync("/api/v1/patients/me");
        using var other = await clientA.GetAsync($"/api/v1/patients/{testerB.ProfileId.Value:D}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);

        using var startA = await clientA.PostAsJsonAsync(
            "/api/v1/pre-triage/sessions", new { pathway = "HEADACHE" });
        using var startB = await clientB.PostAsJsonAsync(
            "/api/v1/pre-triage/sessions", new { pathway = "HEADACHE" });
        var sessionA = await startA.Content.ReadFromJsonAsync<StartedSession>();
        var sessionB = await startB.Content.ReadFromJsonAsync<StartedSession>();
        Assert.Equal(HttpStatusCode.Created, startA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, startB.StatusCode);
        Assert.Equal(testerA.ProfileId.Value, sessionA!.PatientId);
        Assert.Equal(testerB.ProfileId.Value, sessionB!.PatientId);
        using var crossSession = await clientA.GetAsync(
            $"/api/v1/pre-triage/sessions/{sessionB.SessionId:D}/conversation");
        Assert.Equal(HttpStatusCode.NotFound, crossSession.StatusCode);

        await using var db = CreateDbContext();
        Assert.Equal(2, await db.PrivateAccessSessions.CountAsync(value =>
            value.Status == PrivateAccessSessionStatus.Active &&
            (value.CredentialId == testerA.CredentialId ||
             value.CredentialId == testerB.CredentialId)));
        Assert.Equal(2, await db.RefreshSessions.CountAsync(value =>
            value.Status == RefreshSessionStatus.Active &&
            (value.AccountId == testerA.AccountId || value.AccountId == testerB.AccountId)));
        Assert.DoesNotContain(Password, string.Join('|', await db.PrivateAccessCredentials
            .Select(value => value.PasswordHash)
            .ToListAsync()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledCredentialAndAccount_InvalidateGateSessionImmediately()
    {
        await EnsureMigratedAsync();
        var tester = await AddTesterAsync("database-disabled");
        using var factory = Factory();
        using var client = factory.CreateApiClient();
        var authentication = await LoginAsync(client, tester.Username);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authentication.AccessToken);

        await using (var db = CreateDbContext())
        {
            var credential = await db.PrivateAccessCredentials.SingleAsync(
                value => value.Id == tester.CredentialId);
            var account = await db.Accounts.SingleAsync(value => value.Id == tester.AccountId);
            credential.Disable(DateTimeOffset.UtcNow);
            account.Disable(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        using var blocked = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
    }

    [Fact]
    public async Task PrivateLogout_RevokesPrivateSessionAndLinkedRefreshFamily()
    {
        await EnsureMigratedAsync();
        var tester = await AddTesterAsync("database-logout");
        using var factory = Factory();
        using var client = factory.CreateApiClient();
        var authentication = await LoginAsync(client, tester.Username);

        using var logout = await client.PostAsync("/api/v1/private-access/logout", null);
        using var gated = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, gated.StatusCode);

        await using var db = CreateDbContext();
        Assert.Equal(PrivateAccessSessionStatus.Revoked,
            (await db.PrivateAccessSessions.SingleAsync(value =>
                value.CredentialId == tester.CredentialId)).Status);
        Assert.All(
            await db.RefreshSessions.Where(value => value.AccountId == tester.AccountId).ToListAsync(),
            value => Assert.Equal(RefreshSessionStatus.Revoked, value.Status));
        Assert.NotEmpty(authentication.RefreshToken);
    }

    private BeeexyApiFactory Factory() => new(
        postgres.ConnectionString,
        configurationOverrides: new Dictionary<string, string?>
        {
            ["PrivateAccess:Enabled"] = "true",
            ["PrivateAccess:AuthenticationMode"] = "Database",
            ["PrivateAccess:SessionLifetimeMinutes"] = "30",
            ["PrivateAccess:LoginPermitLimit"] = "100",
            ["PrivateAccess:LoginRateLimitWindowMinutes"] = "15",
            ["PrivateAccess:DemoGuest:Enabled"] = "false"
        });

    private static async Task<AuthenticationResponse> LoginAsync(HttpClient client, string username)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/private-access/login",
            new { username, password = Password, keyword = Keyword });
        var body = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<AuthenticationResponse>(body);
    }

    private async Task<TesterIdentity> AddTesterAsync(string prefix)
    {
        await using var db = CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var account = Account.Create(NormalizedEmail.Create($"{prefix}-{suffix}@example.com"), now);
        var profileId = EntityId.New();
        var profile = PatientProfile.Create(
            BeeexyId.Create($"BXY-{profileId.Value:N}".ToUpperInvariant()),
            now,
            account.Id,
            profileId);
        var preference = UserPreference.Create(account.Id, UserTimeZone.Create("America/Lima"), now);
        var username = $"{prefix}-{suffix}";
        var hasher = new Pbkdf2PrivateAccessSecretHasher();
        var credential = PrivateAccessCredential.Create(
            account.Id,
            $"{prefix}-{suffix}",
            username,
            hasher.Hash(Password),
            hasher.Hash(Keyword),
            now);
        db.AddRange(account, profile, preference, credential);
        await db.SaveChangesAsync();
        return new TesterIdentity(credential.Id, account.Id, profile.Id, username);
    }

    private BeeexyDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BeeexyDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task EnsureMigratedAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    private sealed record TesterIdentity(
        EntityId CredentialId,
        EntityId AccountId,
        EntityId ProfileId,
        string Username);

    private sealed record AuthenticationResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt,
        DateTimeOffset RefreshTokenExpiresAt,
        AccountSummaryResponse Account);

    private sealed record AccountSummaryResponse(Guid AccountId, Guid ProfileId, string BeeexyId);

    private sealed record StartedSession(Guid SessionId, Guid? PatientId);
}
