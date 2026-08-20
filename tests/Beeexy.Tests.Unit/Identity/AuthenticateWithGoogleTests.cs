using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Identity;

public sealed class AuthenticateWithGoogleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 19, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NewVerifiedIdentity_UsesSharedProvisioningAndTokenIssuance()
    {
        var email = NormalizedEmail.Create("new-google@example.com");
        var identityRepository = new StubIdentityRepository();
        var provisioningRepository = new StubProvisioningRepository();
        var sessionRepository = new StubSessionRepository();
        var transaction = new StubTransaction();
        var useCase = CreateUseCase(
            new StubProvider(new ValidatedExternalIdentity("google", "google-subject", email)),
            identityRepository,
            provisioningRepository,
            sessionRepository,
            transaction);

        var result = await useCase.ExecuteAsync(
            new AuthenticateWithGoogleCommand("signed-google-id-token"));

        var account = Assert.Single(provisioningRepository.AddedAccounts);
        var profile = Assert.Single(provisioningRepository.AddedProfiles);
        var identity = Assert.Single(identityRepository.AddedIdentities);
        var session = Assert.Single(sessionRepository.AddedSessions);
        Assert.Equal(account.Id, profile.AccountId);
        Assert.Equal(account.Id, identity.AccountId);
        Assert.Equal(account.Id, session.AccountId);
        Assert.Equal(account.Id, result.AccountId);
        Assert.Equal(profile.Id, result.ProfileId);
        Assert.Equal(1, transaction.SaveCount);
        Assert.Equal(1, transaction.CommitCount);
    }

    [Fact]
    public async Task UnknownIdentityWithoutVerifiedEmail_DoesNotProvisionOrIssueSession()
    {
        var identityRepository = new StubIdentityRepository();
        var provisioningRepository = new StubProvisioningRepository();
        var sessionRepository = new StubSessionRepository();
        var transaction = new StubTransaction();
        var useCase = CreateUseCase(
            new StubProvider(new ValidatedExternalIdentity("google", "google-subject", null)),
            identityRepository,
            provisioningRepository,
            sessionRepository,
            transaction);

        await Assert.ThrowsAsync<ExternalIdentityAuthenticationException>(() =>
            useCase.ExecuteAsync(new AuthenticateWithGoogleCommand("signed-google-id-token")));

        Assert.Empty(provisioningRepository.AddedAccounts);
        Assert.Empty(identityRepository.AddedIdentities);
        Assert.Empty(sessionRepository.AddedSessions);
        Assert.Equal(0, transaction.CommitCount);
    }

    [Fact]
    public async Task KnownSubjectWithoutVerifiedEmail_UsesAuthoritativeExistingLink()
    {
        var account = Account.Create(NormalizedEmail.Create("known@example.com"), Now);
        var profile = PatientProfile.Create(
            BeeexyId.Create("BXY-KNOWN-GOOGLE"),
            Now,
            account.Id);
        var identity = ExternalIdentity.Create(account.Id, "google", "known-subject", Now);
        var identityRepository = new StubIdentityRepository(identity, account, profile);
        var sessionRepository = new StubSessionRepository();
        var transaction = new StubTransaction();
        var useCase = CreateUseCase(
            new StubProvider(new ValidatedExternalIdentity("google", "known-subject", null)),
            identityRepository,
            new StubProvisioningRepository(),
            sessionRepository,
            transaction);

        var result = await useCase.ExecuteAsync(
            new AuthenticateWithGoogleCommand("signed-google-id-token"));

        Assert.Equal(account.Id, result.AccountId);
        Assert.Empty(identityRepository.AddedIdentities);
        Assert.Single(sessionRepository.AddedSessions);
        Assert.Equal(1, transaction.CommitCount);
    }

    [Fact]
    public async Task ExistingSubjectAndDifferentEmailAccount_FailsWithoutReassignment()
    {
        var first = Account.Create(NormalizedEmail.Create("first@example.com"), Now);
        var second = Account.Create(NormalizedEmail.Create("second@example.com"), Now);
        var profile = PatientProfile.Create(
            BeeexyId.Create("BXY-FIRST-GOOGLE"),
            Now,
            first.Id);
        var identity = ExternalIdentity.Create(first.Id, "google", "conflict-subject", Now);
        var identityRepository = new StubIdentityRepository(identity, first, profile, second);
        var sessionRepository = new StubSessionRepository();
        var useCase = CreateUseCase(
            new StubProvider(new ValidatedExternalIdentity(
                "google",
                "conflict-subject",
                second.Email)),
            identityRepository,
            new StubProvisioningRepository(),
            sessionRepository,
            new StubTransaction());

        await Assert.ThrowsAsync<ExternalIdentityAuthenticationException>(() =>
            useCase.ExecuteAsync(new AuthenticateWithGoogleCommand("signed-google-id-token")));

        Assert.Equal(first.Id, identity.AccountId);
        Assert.Empty(sessionRepository.AddedSessions);
    }

    [Fact]
    public async Task EmptyCredential_IsRejectedBeforeProviderOrTransaction()
    {
        var provider = new StubProvider(new ValidatedExternalIdentity(
            "google",
            "subject",
            NormalizedEmail.Create("person@example.com")));
        var transaction = new StubTransaction();
        var useCase = CreateUseCase(
            provider,
            new StubIdentityRepository(),
            new StubProvisioningRepository(),
            new StubSessionRepository(),
            transaction);

        await Assert.ThrowsAsync<Beeexy.Application.Common.RequestValidationException>(() =>
            useCase.ExecuteAsync(new AuthenticateWithGoogleCommand(" ")));

        Assert.Equal(0, provider.ValidationCount);
        Assert.Equal(0, transaction.BeginCount);
    }

    private static AuthenticateWithGoogle CreateUseCase(
        IExternalIdentityProvider provider,
        StubIdentityRepository identityRepository,
        StubProvisioningRepository provisioningRepository,
        StubSessionRepository sessionRepository,
        StubTransaction transaction)
    {
        var provision = new ProvisionAccountAndPrimaryProfile(provisioningRepository);
        var tokenIssuer = new IssueAuthenticationTokens(
            new AuthenticationTokenPolicy(
                "https://issuer.test",
                "audience-test",
                "unit-test-signing-key-with-at-least-32-bytes",
                TimeSpan.FromMinutes(15),
                TimeSpan.FromDays(30)),
            new StubAccessTokenIssuer(),
            new StubRefreshTokenService(),
            sessionRepository);
        return new AuthenticateWithGoogle(
            new StubClock(),
            provider,
            identityRepository,
            transaction,
            provision,
            tokenIssuer);
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class StubProvider(ValidatedExternalIdentity result)
        : IExternalIdentityProvider
    {
        public string Provider => "google";

        public bool IsEnabled => true;

        public int ValidationCount { get; private set; }

        public Task<ValidatedExternalIdentity> ValidateAsync(
            string credential,
            CancellationToken cancellationToken = default)
        {
            ValidationCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class StubIdentityRepository : IExternalIdentityAuthenticationRepository
    {
        private readonly List<ExternalIdentity> _identities = [];
        private readonly List<Account> _accounts = [];
        private readonly List<PatientProfile> _profiles = [];

        public StubIdentityRepository(params object[] entities)
        {
            _identities.AddRange(entities.OfType<ExternalIdentity>());
            _accounts.AddRange(entities.OfType<Account>());
            _profiles.AddRange(entities.OfType<PatientProfile>());
        }

        public List<ExternalIdentity> AddedIdentities { get; } = [];

        public Task AcquireIdentityLockAsync(
            string provider,
            string subject,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ExternalIdentity?> FindIdentityAsync(
            string provider,
            string subject,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_identities.SingleOrDefault(x =>
                x.Provider == provider && x.Subject == subject));

        public Task<Account?> FindAccountAsync(
            EntityId accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_accounts.SingleOrDefault(x => x.Id == accountId));

        public Task<Account?> FindAccountAsync(
            NormalizedEmail email,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_accounts.SingleOrDefault(x => x.Email == email));

        public Task<PatientProfile?> FindPrimaryProfileAsync(
            EntityId accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_profiles.SingleOrDefault(x => x.AccountId == accountId));

        public void Add(ExternalIdentity identity)
        {
            AddedIdentities.Add(identity);
            _identities.Add(identity);
        }
    }

    private sealed class StubProvisioningRepository : IAccountProvisioningRepository
    {
        public List<Account> AddedAccounts { get; } = [];

        public List<PatientProfile> AddedProfiles { get; } = [];

        public Task AcquireEmailLockAsync(
            NormalizedEmail email,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Account?> FindAccountAsync(
            NormalizedEmail email,
            CancellationToken cancellationToken = default) => Task.FromResult<Account?>(null);

        public Task<PatientProfile?> FindPrimaryProfileAsync(
            Account account,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PatientProfile?>(null);

        public void Add(Account account, PatientProfile profile, UserPreference preference)
        {
            AddedAccounts.Add(account);
            AddedProfiles.Add(profile);
        }
    }

    private sealed class StubSessionRepository : IRefreshSessionRepository
    {
        public List<RefreshSession> AddedSessions { get; } = [];

        public void Add(RefreshSession session) => AddedSessions.Add(session);

        public Task<RefreshSession?> FindByTokenHashForUpdateAsync(
            TokenHash tokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RefreshSession?>(null);

        public Task<RefreshSession?> FindByIdForUpdateAsync(
            EntityId sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RefreshSession?>(null);

        public Task<Account?> FindAccountAsync(
            EntityId accountId,
            CancellationToken cancellationToken = default) => Task.FromResult<Account?>(null);

        public Task<PatientProfile?> FindPrimaryProfileAsync(
            EntityId accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PatientProfile?>(null);

        public Task RevokeFamilyAsync(
            EntityId familyId,
            DateTimeOffset revokedAt,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubTransaction : IIdentityVerificationTransaction
    {
        public int BeginCount { get; private set; }

        public int SaveCount { get; private set; }

        public int CommitCount { get; private set; }

        public Task BeginAsync(CancellationToken cancellationToken = default)
        {
            BeginCount++;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubAccessTokenIssuer : IAccessTokenIssuer
    {
        public IssuedAccessToken Issue(
            EntityId accountId,
            EntityId sessionId,
            DateTimeOffset issuedAt) =>
            new("unit-access-token", issuedAt.AddMinutes(15));
    }

    private sealed class StubRefreshTokenService : IRefreshTokenService
    {
        public GeneratedRefreshToken Generate() =>
            new("unit-refresh-token", TokenHash.FromHash("unit-refresh-token-hash"));

        public TokenHash Hash(string refreshToken) =>
            TokenHash.FromHash("unit-refresh-token-hash");
    }
}
