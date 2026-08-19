using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Tests.Unit.Identity;

public sealed class RequestEmailChallengeTests
{
    private const string OneTimeCode = "583104";
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidRequest_PersistsNormalizedPendingChallengeThenSendsOtp()
    {
        var calls = new List<string>();
        var repository = new RecordingRepository(calls);
        var sender = new RecordingSender(calls);
        var hasher = new RecordingHasher();
        var useCase = CreateUseCase(repository, sender, hasher: hasher);

        var result = await useCase.ExecuteAsync(
            new RequestEmailChallengeCommand(" Person@Example.COM ", "192.0.2.1"));

        Assert.Same(RequestEmailChallengeResult.Accepted, result);
        var challenge = Assert.IsType<EmailAuthenticationChallenge>(repository.Persisted);
        Assert.Equal("person@example.com", challenge.Email.Value);
        Assert.Equal(ChallengeStatus.Pending, challenge.Status);
        Assert.Equal(0, challenge.AttemptCount);
        Assert.Equal(Now.AddMinutes(10), challenge.ExpiresAt);
        Assert.Equal("test-hash", challenge.OtpHash.Value);
        Assert.DoesNotContain(OneTimeCode, challenge.OtpHash.Value, StringComparison.Ordinal);
        Assert.Equal(challenge.Id, hasher.ChallengeId);
        Assert.Equal(OneTimeCode, hasher.OneTimeCode);

        var message = Assert.IsType<AuthenticationEmailMessage>(sender.Message);
        Assert.Equal(challenge.Email, message.Recipient);
        Assert.Equal(OneTimeCode, message.OneTimeCode);
        Assert.Equal(challenge.ExpiresAt, message.ExpiresAt);
        Assert.Equal(["persist", "send"], calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task InvalidEmail_IsRejectedBeforeRateLimitOrPersistence(string? email)
    {
        var rateLimiter = new RecordingRateLimiter();
        var repository = new RecordingRepository([]);
        var sender = new RecordingSender([]);
        var useCase = CreateUseCase(repository, sender, rateLimiter: rateLimiter);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(new RequestEmailChallengeCommand(email, "192.0.2.1")));

        Assert.Equal("authentication.invalid_email", exception.Code);
        Assert.False(rateLimiter.WasCalled);
        Assert.Null(repository.Persisted);
        Assert.Null(sender.Message);
    }

    [Fact]
    public async Task ThrottledRequest_DoesNotGeneratePersistOrSendChallenge()
    {
        var repository = new RecordingRepository([]);
        var sender = new RecordingSender([]);
        var generator = new RecordingGenerator();
        var rateLimiter = new RecordingRateLimiter
        {
            Result = EmailChallengeRateLimitResult.Rejected(TimeSpan.FromMinutes(3))
        };
        var useCase = CreateUseCase(
            repository,
            sender,
            generator: generator,
            rateLimiter: rateLimiter);

        var exception = await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            useCase.ExecuteAsync(
                new RequestEmailChallengeCommand("person@example.com", "192.0.2.1")));

        Assert.Equal(TimeSpan.FromMinutes(3), exception.RetryAfter);
        Assert.False(generator.WasCalled);
        Assert.Null(repository.Persisted);
        Assert.Null(sender.Message);
    }

    [Fact]
    public async Task SenderFailure_RemovesChallengeAndDoesNotRetainSecretInException()
    {
        var repository = new RecordingRepository([]);
        var sender = new RecordingSender([], shouldFail: true);
        var useCase = CreateUseCase(repository, sender);

        var exception = await Assert.ThrowsAsync<AuthenticationEmailDeliveryException>(() =>
            useCase.ExecuteAsync(
                new RequestEmailChallengeCommand("person@example.com", "192.0.2.1")));

        Assert.Equal(repository.Persisted?.Id, repository.DeletedId);
        Assert.DoesNotContain(OneTimeCode, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("test-hash", exception.ToString(), StringComparison.Ordinal);
    }

    private static RequestEmailChallenge CreateUseCase(
        RecordingRepository repository,
        RecordingSender sender,
        RecordingHasher? hasher = null,
        RecordingGenerator? generator = null,
        RecordingRateLimiter? rateLimiter = null)
    {
        return new RequestEmailChallenge(
            new StubClock(Now),
            new EmailChallengePolicy(
                6,
                TimeSpan.FromMinutes(10),
                3,
                20,
                TimeSpan.FromMinutes(15)),
            generator ?? new RecordingGenerator(),
            hasher ?? new RecordingHasher(),
            rateLimiter ?? new RecordingRateLimiter(),
            repository,
            sender);
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RecordingGenerator : IOneTimePasswordGenerator
    {
        public bool WasCalled { get; private set; }

        public string Generate(int length)
        {
            WasCalled = true;
            Assert.Equal(6, length);
            return OneTimeCode;
        }
    }

    private sealed class RecordingHasher : IOneTimePasswordHasher
    {
        public EntityId? ChallengeId { get; private set; }

        public string? OneTimeCode { get; private set; }

        public TokenHash Hash(EntityId challengeId, string oneTimeCode)
        {
            ChallengeId = challengeId;
            OneTimeCode = oneTimeCode;
            return TokenHash.FromHash("test-hash");
        }
    }

    private sealed class RecordingRateLimiter : IEmailChallengeRateLimiter
    {
        public bool WasCalled { get; private set; }

        public EmailChallengeRateLimitResult Result { get; init; } =
            EmailChallengeRateLimitResult.Allowed;

        public ValueTask<EmailChallengeRateLimitResult> TryAcquireAsync(
            NormalizedEmail email,
            string requesterIpAddress,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class RecordingRepository(List<string> calls)
        : IEmailAuthenticationChallengeRepository
    {
        public EmailAuthenticationChallenge? Persisted { get; private set; }

        public EntityId? DeletedId { get; private set; }

        public Task ReplacePendingAsync(
            EmailAuthenticationChallenge challenge,
            CancellationToken cancellationToken = default)
        {
            calls.Add("persist");
            Persisted = challenge;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            EntityId challengeId,
            CancellationToken cancellationToken = default)
        {
            DeletedId = challengeId;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSender(List<string> calls, bool shouldFail = false)
        : IAuthenticationEmailSender
    {
        public AuthenticationEmailMessage? Message { get; private set; }

        public Task SendAsync(
            AuthenticationEmailMessage message,
            CancellationToken cancellationToken = default)
        {
            calls.Add("send");
            Message = message;
            return shouldFail
                ? throw new InvalidOperationException($"Provider failed for {message.OneTimeCode}")
                : Task.CompletedTask;
        }
    }
}
