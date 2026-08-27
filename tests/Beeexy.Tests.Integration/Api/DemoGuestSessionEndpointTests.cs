using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Beeexy.Api.PrivateAccess;
using Beeexy.Application.Identity;
using Beeexy.Application.Interoperability;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Interoperability;
using Beeexy.Domain.Patients;
using Beeexy.Infrastructure.Persistence;
using Beeexy.Tests.Integration.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Beeexy.Tests.Integration.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class DemoGuestSessionEndpointTests(PostgreSqlContainerFixture postgres)
{
    private const string Username = "BeeexyGuestGate";
    private const string Password = "DemoGuestPassword!123";
    private const string Keyword = "BeeexyGuestKeyword";
    private const string GuestSessionEndpoint = "/api/v1/private-access/guest-session";

    [Fact]
    public async Task ValidPrivateAccess_IssuesIndependentNormalSessionsAndSupportsSeparateLogout()
    {
        await EnsureMigratedAsync();
        using var factory = Factory("guest-session-lifecycle");
        using var client = factory.CreateApiClient();
        var provisioned = await ProvisionConfiguredAsync(factory);
        await LoginPrivateAccessAsync(client);

        var first = await CreateGuestSessionAsync(client);
        var second = await CreateGuestSessionAsync(client);

        Assert.Equal(provisioned.AccountId.Value, first.Account.AccountId);
        Assert.Equal(provisioned.ProfileId.Value, first.Account.ProfileId);
        Assert.Equal(provisioned.BeeexyId, first.Account.BeeexyId);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        await using (var db = CreateDbContext())
        {
            Assert.Equal(2, await db.RefreshSessions.CountAsync(session =>
                session.AccountId == provisioned.AccountId &&
                session.Status == RefreshSessionStatus.Active));
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", first.AccessToken);
        using var current = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);

        using var authLogout = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, authLogout.StatusCode);
        using var revokedRefresh = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, revokedRefresh.StatusCode);

        using var privateStatus = await client.GetAsync("/api/v1/private-access/session");
        var privateSession = await privateStatus.Content
            .ReadFromJsonAsync<PrivateSessionStatus>();
        Assert.True(privateSession!.Authenticated);

        client.DefaultRequestHeaders.Authorization = null;
        var replacement = await CreateGuestSessionAsync(client);
        Assert.NotEqual(first.RefreshToken, replacement.RefreshToken);

        using var privateLogout = await client.PostAsync(
            "/api/v1/private-access/logout",
            null);
        using var afterPrivateLogout = await client.PostAsync(GuestSessionEndpoint, null);
        Assert.Equal(HttpStatusCode.NoContent, privateLogout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterPrivateLogout.StatusCode);
        var problem = await afterPrivateLogout.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("Private access required.", problem!.Title);
    }

    [Fact]
    public async Task MissingExpiredAndTamperedPrivateAccess_AreDeniedBeforeSessionIssuance()
    {
        await EnsureMigratedAsync();
        using var factory = Factory("guest-session-gate");
        using var startupClient = factory.CreateApiClient();
        var provisioned = await ProvisionConfiguredAsync(factory);
        var tokenService = factory.Services
            .GetRequiredService<PrivateAccessSessionTokenService>();
        var expired = tokenService.Issue(DateTimeOffset.UtcNow.AddHours(-2)).Token;
        var valid = tokenService.Issue(DateTimeOffset.UtcNow).Token;
        var replacement = valid[^1] == 'A' ? 'B' : 'A';
        var tampered = valid[..^1] + replacement;

        foreach (var token in new string?[] { null, expired, tampered })
        {
            using var client = factory.CreateApiClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, GuestSessionEndpoint);
            if (token is not null)
            {
                request.Headers.TryAddWithoutValidation(
                    "Cookie",
                    $"{PrivateAccessSettings.CookieName}={token}");
            }

            using var response = await client.SendAsync(request);
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Private access required.", problem!.Title);
        }

        await using var db = CreateDbContext();
        Assert.False(await db.RefreshSessions.AnyAsync(
            session => session.AccountId == provisioned.AccountId));
    }

    [Fact]
    public async Task DisabledMissingOrIncompatibleDemoGuest_ReturnsSafeUnavailable()
    {
        await EnsureMigratedAsync();

        using (var disabledFactory = Factory("guest-disabled", demoGuestEnabled: false))
        using (var disabledClient = disabledFactory.CreateApiClient())
        {
            await LoginPrivateAccessAsync(disabledClient);
            using var response = await disabledClient.PostAsync(GuestSessionEndpoint, null);
            await AssertUnavailableAsync(response);
        }

        using (var missingFactory = Factory("guest-missing"))
        using (var missingClient = missingFactory.CreateApiClient())
        {
            await LoginPrivateAccessAsync(missingClient);
            using var response = await missingClient.PostAsync(GuestSessionEndpoint, null);
            await AssertUnavailableAsync(response);
        }

        using (var invalidFactory = Factory("guest-invalid"))
        using (var invalidClient = invalidFactory.CreateApiClient())
        {
            var provisioned = await ProvisionConfiguredAsync(invalidFactory);
            await using (var db = CreateDbContext())
            {
                var profile = await db.PatientProfiles.SingleAsync(
                    candidate => candidate.Id == provisioned.ProfileId);
                profile.UpdateDemographics(
                    PatientName.Create("Changed"),
                    null,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
            }

            await LoginPrivateAccessAsync(invalidClient);
            using var response = await invalidClient.PostAsync(GuestSessionEndpoint, null);
            await AssertUnavailableAsync(response);
        }
    }

    [Fact]
    public async Task GuestSessionRejectsEveryCallerSelectedIdentityField()
    {
        await EnsureMigratedAsync();
        using var factory = Factory("guest-no-impersonation");
        using var client = factory.CreateApiClient();
        var provisioned = await ProvisionConfiguredAsync(factory);
        await LoginPrivateAccessAsync(client);

        using var body = await client.PostAsJsonAsync(GuestSessionEndpoint, new
        {
            accountId = Guid.NewGuid(),
            patientId = Guid.NewGuid(),
            beeexyId = "BXY-ATTACKER",
            email = "attacker@example.com",
            role = "admin"
        });
        using var query = await client.PostAsync(
            $"{GuestSessionEndpoint}?accountId={Guid.NewGuid():D}",
            null);

        Assert.Equal(HttpStatusCode.BadRequest, body.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        await using var db = CreateDbContext();
        Assert.False(await db.RefreshSessions.AnyAsync(
            session => session.AccountId == provisioned.AccountId));
    }

    [Fact]
    public async Task IssuedGuestIdentity_CompletesProfileTriageHistoryAndFhirJourney()
    {
        await EnsureMigratedAsync();
        var artifactStore = new MemoryArtifactStore();
        using var factory = Factory("guest-downstream", artifactStore: artifactStore);
        using var client = factory.CreateApiClient();
        var provisioned = await ProvisionConfiguredAsync(factory);
        await LoginPrivateAccessAsync(client);
        var authentication = await CreateGuestSessionAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authentication.AccessToken);

        using var accountResponse = await client.GetAsync("/api/v1/auth/me");
        var account = await accountResponse.Content.ReadFromJsonAsync<CurrentAccountResponse>();
        Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);
        Assert.Equal(provisioned.AccountId.Value, account!.AccountId);
        Assert.Equal(provisioned.ProfileId.Value, account.PrimaryProfile.ProfileId);

        using var profileResponse = await client.GetAsync("/api/v1/patients/me");
        var profile = await profileResponse.Content.ReadFromJsonAsync<PrimaryProfileResponse>();
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        Assert.Equal("Bee", profile!.FirstName);
        Assert.Equal("Exy", profile.LastName);
        Assert.Equal(new DateOnly(1990, 5, 20), profile.DateOfBirth);
        Assert.Equal("Female", profile.SexAssignedAtBirth);
        Assert.Equal("CA", profile.State);
        Assert.Equal("America/Lima", profile.Preferences.Timezone);

        using var start = await client.PostAsJsonAsync(
            "/api/v1/pre-triage/sessions",
            new { pathway = "HEADACHE" });
        var started = await start.Content.ReadFromJsonAsync<StartedSession>();
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        Assert.Equal(provisioned.ProfileId.Value, started!.PatientId);
        Assert.Equal("IN_PROGRESS", started.Conversation.State);
        Assert.Equal("ACTIVE", started.Conversation.SessionStatus);
        Assert.Equal(new ConversationProgress(0, 3, 0), started.Conversation.Progress);
        Assert.Equal("duration", started.Conversation.NextInteraction!.Field);

        using var answer = await client.PostAsJsonAsync(
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/answers",
            new
            {
                structured = new
                {
                    duration = new { value = 2, unit = "DAYS" },
                    intensity = 7,
                    additionalSymptoms = new[] { "FEVER" }
                }
            });
        var answered = await answer.Content.ReadFromJsonAsync<AnswerWithConversation>();
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("READY_FOR_REVIEW", answered!.Conversation.State);
        Assert.Equal("ACTIVE", answered.Conversation.SessionStatus);
        Assert.Equal(new ConversationProgress(3, 3, 100),
            answered.Conversation.Progress);
        Assert.Null(answered.Conversation.NextInteraction);
        Assert.Equal(["FEVER"],
            answered.Conversation.AcceptedValues.AdditionalSymptoms);

        var conversationEndpoint =
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/conversation";
        using var readyRead = await client.GetAsync(conversationEndpoint);
        using var repeatedReadyRead = await client.GetAsync(conversationEndpoint);
        var ready = await readyRead.Content.ReadFromJsonAsync<ConversationResponse>();
        var repeatedReady = await repeatedReadyRead.Content
            .ReadFromJsonAsync<ConversationResponse>();
        Assert.Equal(HttpStatusCode.OK, readyRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeatedReadyRead.StatusCode);
        Assert.Equal("READY_FOR_REVIEW", ready!.State);
        Assert.Equal(ready.State, repeatedReady!.State);
        Assert.Equal(ready.SessionStatus, repeatedReady.SessionStatus);
        Assert.Equal(ready.Progress, repeatedReady.Progress);
        Assert.Equal(ready.AcceptedValues.Duration,
            repeatedReady.AcceptedValues.Duration);
        Assert.Equal(ready.AcceptedValues.Intensity,
            repeatedReady.AcceptedValues.Intensity);
        Assert.Equal(ready.AcceptedValues.AdditionalSymptoms,
            repeatedReady.AcceptedValues.AdditionalSymptoms);
        Assert.Null(repeatedReady.NextInteraction);
        await using (var active = CreateDbContext())
        {
            Assert.Equal(0, await active.ClinicalHistoryEvents.CountAsync(value =>
                value.PatientProfileId == provisioned.ProfileId));
            Assert.Equal(0, await active.FhirExports.CountAsync(value =>
                value.PatientProfileId == provisioned.ProfileId));
        }

        using var complete = await client.PostAsync(
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/complete",
            null);
        var completed = await complete.Content.ReadFromJsonAsync<CompletedSession>();
        Assert.Equal(HttpStatusCode.Created, complete.StatusCode);
        using var completedRead = await client.GetAsync(conversationEndpoint);
        var completedConversation = await completedRead.Content
            .ReadFromJsonAsync<ConversationResponse>();
        using var repeatedComplete = await client.PostAsync(
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/complete",
            null);
        var repeatedCompletion = await repeatedComplete.Content
            .ReadFromJsonAsync<CompletedSession>();
        using var canonicalResult = await client.GetAsync(
            $"/api/v1/pre-triage/sessions/{started.SessionId:D}/result");
        var retrieved = await canonicalResult.Content
            .ReadFromJsonAsync<CompletedSession>();
        Assert.Equal(HttpStatusCode.OK, completedRead.StatusCode);
        Assert.Equal("COMPLETED", completedConversation!.State);
        Assert.Equal("COMPLETED", completedConversation.SessionStatus);
        Assert.Null(completedConversation.NextInteraction);
        Assert.Equal(HttpStatusCode.OK, repeatedComplete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, canonicalResult.StatusCode);
        Assert.Equal(completed!.EpisodeId, repeatedCompletion!.EpisodeId);
        Assert.Equal(completed.EpisodeId, retrieved!.EpisodeId);

        var historyEndpoint =
            $"/api/v1/patients/{provisioned.ProfileId.Value:D}/clinical-history";
        using var historyResponse = await client.GetAsync(historyEndpoint);
        var history = await historyResponse.Content.ReadFromJsonAsync<HistoryPage>();
        var historyItem = Assert.Single(
            history!.Items,
            item => item.Source.Id == completed!.EpisodeId);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        using var detail = await client.GetAsync(
            $"{historyEndpoint}/{historyItem.EventId:D}");
        var historyDetail = await detail.Content.ReadFromJsonAsync<HistoryDetail>();
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal("HEADACHE", historyDetail!.PrimarySymptom.Code);
        Assert.Equal(new DurationValue(2, "DAYS"), historyDetail.Duration);
        Assert.Equal(7, historyDetail.Intensity);
        Assert.Equal(["FEVER"], historyDetail.AdditionalSymptoms);

        using var createExport = await client.PostAsJsonAsync(
            $"/api/v1/patients/{provisioned.ProfileId.Value:D}/fhir-exports",
            new
            {
                sourceClinicalHistoryEventId = historyItem.EventId,
                idempotencyKey = Guid.NewGuid()
            });
        var export = await createExport.Content.ReadFromJsonAsync<FhirExportResponse>();
        Assert.Equal(HttpStatusCode.Created, createExport.StatusCode);
        Assert.Equal("Validated", export!.Status);
        using var metadata = await client.GetAsync($"/api/v1/fhir-exports/{export.Id:D}");
        using var download = await client.GetAsync(
            $"/api/v1/fhir-exports/{export.Id:D}/content");
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(FhirR4BaseMvp.MediaType, download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(1, artifactStore.Count);

        var unrelated = await ProvisionNormalIdentityAsync(factory);
        using var unrelatedProfile = await client.GetAsync(
            $"/api/v1/patients/{unrelated.PrimaryProfile.Id.Value:D}");
        Assert.Equal(HttpStatusCode.NotFound, unrelatedProfile.StatusCode);
    }

    private BeeexyApiFactory Factory(
        string prefix,
        bool demoGuestEnabled = true,
        MemoryArtifactStore? artifactStore = null)
    {
        var settings = EnabledSettings(prefix, demoGuestEnabled);
        return new BeeexyApiFactory(
            postgres.ConnectionString,
            configurationOverrides: settings,
            configureServices: artifactStore is null
                ? null
                : services =>
                {
                    services.RemoveAll<IFhirArtifactStore>();
                    services.AddSingleton<IFhirArtifactStore>(artifactStore);
                });
    }

    private static Dictionary<string, string?> EnabledSettings(
        string prefix,
        bool demoGuestEnabled)
    {
        var values = new Dictionary<string, string?>
        {
            ["PrivateAccess:Enabled"] = "true",
            ["PrivateAccess:Username"] = Username,
            ["PrivateAccess:PasswordHash"] = PrivateAccessPasswordHasher.Hash(Password),
            ["PrivateAccess:KeywordHash"] = PrivateAccessPasswordHasher.Hash(Keyword),
            ["PrivateAccess:SessionSigningKey"] = Convert.ToBase64String(
                Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()),
            ["PrivateAccess:SessionLifetimeMinutes"] = "30",
            ["PrivateAccess:LoginPermitLimit"] = "20",
            ["PrivateAccess:LoginRateLimitWindowMinutes"] = "15",
            ["PrivateAccess:DemoGuest:Enabled"] = demoGuestEnabled.ToString()
        };

        if (demoGuestEnabled)
        {
            values["PrivateAccess:DemoGuest:Email"] =
                $"{prefix}-{Guid.NewGuid():N}@example.com";
            values["PrivateAccess:DemoGuest:FirstName"] = "Bee";
            values["PrivateAccess:DemoGuest:LastName"] = "Exy";
            values["PrivateAccess:DemoGuest:DateOfBirth"] = "1990-05-20";
            values["PrivateAccess:DemoGuest:SexAssignedAtBirth"] = "Female";
            values["PrivateAccess:DemoGuest:State"] = "CA";
            values["PrivateAccess:DemoGuest:Timezone"] = "America/Lima";
        }

        return values;
    }

    private static async Task LoginPrivateAccessAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/private-access/login",
            new { username = Username, password = Password, keyword = Keyword });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<AuthenticationResponse> CreateGuestSessionAsync(HttpClient client)
    {
        using var response = await client.PostAsync(GuestSessionEndpoint, null);
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return Assert.IsType<AuthenticationResponse>(result);
    }

    private static async Task<ProvisionDemoGuestResult> ProvisionConfiguredAsync(
        BeeexyApiFactory factory)
    {
        var settings = factory.Services.GetRequiredService<PrivateAccessSettings>();
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProvisionDemoGuest>()
            .ExecuteAsync(settings.DemoGuest.Definition!);
    }

    private static async Task<ProvisionedAccountResult> ProvisionNormalIdentityAsync(
        BeeexyApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var transaction = scope.ServiceProvider
            .GetRequiredService<IIdentityVerificationTransaction>();
        var provision = scope.ServiceProvider
            .GetRequiredService<ProvisionAccountAndPrimaryProfile>();
        await transaction.BeginAsync();
        var result = await provision.ExecuteAsync(
            NormalizedEmail.Create($"unrelated-{Guid.NewGuid():N}@example.com"),
            DateTimeOffset.UtcNow);
        await transaction.SaveChangesAsync();
        await transaction.CommitAsync();
        return result;
    }

    private static async Task AssertUnavailableAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Demo Guest unavailable.", problem!.Title);
        Assert.Equal(
            "The Demo Guest authentication session is not available.",
            problem.Detail);
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

    private sealed class MemoryArtifactStore : IFhirArtifactStore
    {
        private readonly ConcurrentDictionary<string, byte[]> _artifacts =
            new(StringComparer.Ordinal);

        public int Count => _artifacts.Count;

        public Task StoreImmutableAsync(
            FhirArtifactStorageReference reference,
            ReadOnlyMemory<byte> artifactBytes,
            CancellationToken cancellationToken = default)
        {
            if (!_artifacts.TryAdd(reference.PrivateUri, artifactBytes.ToArray()))
            {
                throw new FhirArtifactAlreadyExistsException();
            }

            return Task.CompletedTask;
        }

        public Task<byte[]> ReadAsync(
            FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_artifacts[reference.PrivateUri].ToArray());

        public Task<bool> DeleteAsync(
            FhirArtifactStorageReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_artifacts.TryRemove(reference.PrivateUri, out _));
    }

    private sealed record AuthenticationResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt,
        DateTimeOffset RefreshTokenExpiresAt,
        AuthenticationAccount Account);

    private sealed record AuthenticationAccount(
        Guid AccountId,
        Guid ProfileId,
        string BeeexyId);

    private sealed record PrivateSessionStatus(bool Authenticated, DateTimeOffset? ExpiresAt);

    private sealed record ProblemResponse(string Title, string? Detail);

    private sealed record CurrentAccountResponse(
        Guid AccountId,
        CurrentPrimaryProfile PrimaryProfile);

    private sealed record CurrentPrimaryProfile(Guid ProfileId, string BeeexyId);

    private sealed record PrimaryProfileResponse(
        Guid ProfileId,
        string BeeexyId,
        string? FirstName,
        string? LastName,
        DateOnly? DateOfBirth,
        string? SexAssignedAtBirth,
        string? State,
        PrimaryProfilePreferences Preferences);

    private sealed record PrimaryProfilePreferences(string Timezone);

    private sealed record StartedSession(
        Guid SessionId,
        Guid? PatientId,
        ConversationResponse Conversation);

    private sealed record AnswerWithConversation(ConversationResponse Conversation);

    private sealed record ConversationResponse(
        string SessionStatus,
        string State,
        ConversationProgress Progress,
        ConversationAcceptedValues AcceptedValues,
        ConversationInteraction? NextInteraction);

    private sealed record ConversationProgress(int Completed, int Total, int Percentage);

    private sealed record ConversationAcceptedValues(
        DurationValue? Duration,
        int? Intensity,
        IReadOnlyList<string>? AdditionalSymptoms);

    private sealed record ConversationInteraction(string Field);

    private sealed record CompletedSession(Guid EpisodeId);

    private sealed record HistoryPage(IReadOnlyList<HistoryItem> Items);

    private sealed record HistoryItem(Guid EventId, HistorySource Source);

    private sealed record HistorySource(Guid Id);

    private sealed record HistoryDetail(
        PrimarySymptom PrimarySymptom,
        DurationValue Duration,
        int Intensity,
        IReadOnlyList<string> AdditionalSymptoms);

    private sealed record PrimarySymptom(string Code);

    private sealed record DurationValue(decimal Value, string Unit);

    private sealed record FhirExportResponse(Guid Id, string Status);
}
