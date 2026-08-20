using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;
using Beeexy.Domain.Patients;

namespace Beeexy.Tests.Unit.Identity;

public sealed class VerifyEmailChallengeTests
{
    private const string CorrectCode = "583104";
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CorrectCode_ConsumesChallengeAndStagesCompleteIdentityInOneCommit()
    {
        var challenge = CreateChallenge();
        var challengeRepository = new ChallengeRepository(challenge);
        var provisioningRepository = new ProvisioningRepository();
        var transaction = new RecordingTransaction();
        var useCase = CreateUseCase(challengeRepository, provisioningRepository, transaction);

        var result = await useCase.ExecuteAsync(
            new VerifyEmailChallengeCommand(" PERSON@Example.com ", CorrectCode));

        Assert.Equal(ChallengeStatus.Consumed, challenge.Status);
        Assert.Equal(Now, challenge.ConsumedAt);
        Assert.Equal(1, transaction.SaveCount);
        Assert.Equal(1, transaction.CommitCount);
        Assert.NotNull(provisioningRepository.AddedAccount);
        Assert.NotNull(provisioningRepository.AddedProfile);
        Assert.NotNull(provisioningRepository.AddedPreference);
        Assert.Equal(provisioningRepository.AddedAccount.Id, provisioningRepository.AddedProfile.AccountId);
        Assert.Equal(provisioningRepository.AddedAccount.Id, provisioningRepository.AddedPreference.AccountId);
        Assert.Equal("Etc/UTC", provisioningRepository.AddedPreference.TimeZone.Value);
        Assert.Equal(provisioningRepository.AddedAccount.Id, result.AccountId);
        Assert.Equal(provisioningRepository.AddedProfile.Id, result.ProfileId);
    }

    [Fact]
    public async Task WrongCode_IncrementsAttemptCommitsAndDoesNotProvision()
    {
        var challenge = CreateChallenge();
        var challengeRepository = new ChallengeRepository(challenge);
        var provisioningRepository = new ProvisioningRepository();
        var transaction = new RecordingTransaction();
        var useCase = CreateUseCase(challengeRepository, provisioningRepository, transaction);

        await Assert.ThrowsAsync<EmailChallengeUnauthorizedException>(() =>
            useCase.ExecuteAsync(new VerifyEmailChallengeCommand(
                "person@example.com",
                "000000")));

        Assert.Equal(1, challenge.AttemptCount);
        Assert.Equal(ChallengeStatus.Pending, challenge.Status);
        Assert.Equal(1, transaction.SaveCount);
        Assert.Equal(1, transaction.CommitCount);
        Assert.Null(provisioningRepository.AddedAccount);
    }

    [Fact]
    public async Task ExpiredChallenge_IsPersistedAndCannotProvision()
    {
        var challenge = EmailAuthenticationChallenge.Create(
            NormalizedEmail.Create("person@example.com"),
            TokenHash.FromHash("matching-hash"),
            Now,
            Now.AddMinutes(-10));
        var challengeRepository = new ChallengeRepository(challenge);
        var provisioningRepository = new ProvisioningRepository();
        var transaction = new RecordingTransaction();
        var useCase = CreateUseCase(challengeRepository, provisioningRepository, transaction);

        await Assert.ThrowsAsync<EmailChallengeUnauthorizedException>(() =>
            useCase.ExecuteAsync(new VerifyEmailChallengeCommand(
                "person@example.com",
                CorrectCode)));

        Assert.Equal(ChallengeStatus.Expired, challenge.Status);
        Assert.Equal(1, transaction.CommitCount);
        Assert.Null(provisioningRepository.AddedAccount);
    }

    [Fact]
    public async Task ConsumedChallenge_IsRejectedAsReplayWithoutAnotherCommit()
    {
        var challenge = CreateChallenge();
        challenge.Consume(Now.AddMinutes(-1));
        var transaction = new RecordingTransaction();
        var useCase = CreateUseCase(
            new ChallengeRepository(challenge),
            new ProvisioningRepository(),
            transaction);

        await Assert.ThrowsAsync<EmailChallengeReplayException>(() =>
            useCase.ExecuteAsync(new VerifyEmailChallengeCommand(
                "person@example.com",
                CorrectCode)));

        Assert.Equal(0, transaction.SaveCount);
        Assert.Equal(0, transaction.CommitCount);
    }

    [Fact]
    public async Task AttemptLimit_BlocksEvenCorrectCode()
    {
        var challenge = CreateChallenge();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            challenge.RecordFailedAttempt(Now.AddMinutes(-2 + attempt));
        }

        var useCase = CreateUseCase(
            new ChallengeRepository(challenge),
            new ProvisioningRepository(),
            new RecordingTransaction(),
            maximumAttempts: 2);

        await Assert.ThrowsAsync<EmailChallengeAttemptLimitException>(() =>
            useCase.ExecuteAsync(new VerifyEmailChallengeCommand(
                "person@example.com",
                CorrectCode)));
    }

    private static VerifyEmailChallenge CreateUseCase(
        ChallengeRepository challengeRepository,
        ProvisioningRepository provisioningRepository,
        RecordingTransaction transaction,
        int maximumAttempts = 5)
    {
        var provision = new ProvisionAccountAndPrimaryProfile(provisioningRepository);
        var tokenPolicy = new AuthenticationTokenPolicy(
            "unit-test-issuer",
            "unit-test-audience",
            "unit-test-signing-key-with-at-least-32-bytes",
            TimeSpan.FromMinutes(15),
            TimeSpan.FromDays(30));
        var tokenIssuer = new IssueAuthenticationTokens(
            tokenPolicy,
            new StubAccessTokenIssuer(),
            new StubRefreshTokenService(),
            new SessionRepository());
        return new VerifyEmailChallenge(
            new StubClock(),
            new EmailChallengePolicy(
                6,
                TimeSpan.FromMinutes(10),
                3,
                20,
                TimeSpan.FromMinutes(15),
                maximumAttempts),
            new StubHasher(),
            challengeRepository,
            transaction,
            provision,
            tokenIssuer);
    }

    private static EmailAuthenticationChallenge CreateChallenge()
    {
        return EmailAuthenticationChallenge.Create(
            NormalizedEmail.Create("person@example.com"),
            TokenHash.FromHash("matching-hash"),
            Now.AddMinutes(5),
            Now.AddMinutes(-5));
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class StubHasher : IOneTimePasswordHasher
    {
        public TokenHash Hash(EntityId challengeId, string oneTimeCode)
        {
            return TokenHash.FromHash(
                oneTimeCode == CorrectCode ? "matching-hash" : "different-hash");
        }
    }

    private sealed class StubAccessTokenIssuer : IAccessTokenIssuer
    {
        public IssuedAccessToken Issue(
            EntityId accountId,
            EntityId sessionId,
            DateTimeOffset issuedAt) => new("access-token", issuedAt.AddMinutes(15));
    }

    private sealed class StubRefreshTokenService : IRefreshTokenService
    {
        public GeneratedRefreshToken Generate() =>
            new("rt1.unit-test", TokenHash.FromHash("refresh-hash"));

        public TokenHash Hash(string refreshToken) => TokenHash.FromHash("refresh-hash");
    }

    private sealed class SessionRepository : IRefreshSessionRepository
    {
        public void Add(RefreshSession session)
        {
        }

        public Task<RefreshSession?> FindByTokenHashForUpdateAsync(
            TokenHash tokenHash,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RefreshSession?> FindByIdForUpdateAsync(
            EntityId sessionId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Account?> FindAccountAsync(
            EntityId accountId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PatientProfile?> FindPrimaryProfileAsync(
            EntityId accountId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RevokeFamilyAsync(
            EntityId familyId,
            DateTimeOffset revokedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ChallengeRepository(EmailAuthenticationChallenge challenge)
        : IEmailAuthenticationChallengeRepository
    {
        public Task<EmailAuthenticationChallenge?> FindLatestForUpdateAsync(
            NormalizedEmail email,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<EmailAuthenticationChallenge?>(challenge);
        }

        public Task ReplacePendingAsync(
            EmailAuthenticationChallenge replacement,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(
            EntityId challengeId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ProvisioningRepository : IAccountProvisioningRepository
    {
        public Account? AddedAccount { get; private set; }

        public PatientProfile? AddedProfile { get; private set; }

        public UserPreference? AddedPreference { get; private set; }

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
            AddedAccount = account;
            AddedProfile = profile;
            AddedPreference = preference;
        }
    }

    private sealed class RecordingTransaction : IIdentityVerificationTransaction
    {
        public int SaveCount { get; private set; }

        public int CommitCount { get; private set; }

        public Task BeginAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

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
}
