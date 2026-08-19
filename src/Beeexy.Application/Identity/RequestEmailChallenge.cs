using Beeexy.Application.Common;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public sealed class RequestEmailChallenge(
    IClock clock,
    EmailChallengePolicy policy,
    IOneTimePasswordGenerator passwordGenerator,
    IOneTimePasswordHasher passwordHasher,
    IEmailChallengeRateLimiter rateLimiter,
    IEmailAuthenticationChallengeRepository challengeRepository,
    IAuthenticationEmailSender emailSender)
{
    public async Task<RequestEmailChallengeResult> ExecuteAsync(
        RequestEmailChallengeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        NormalizedEmail normalizedEmail;
        try
        {
            normalizedEmail = NormalizedEmail.Create(command.Email ?? string.Empty);
        }
        catch (ArgumentException)
        {
            throw new RequestValidationException(
                "authentication.invalid_email",
                "A valid email address is required.");
        }

        var requesterIpAddress = string.IsNullOrWhiteSpace(command.RequesterIpAddress)
            ? "unavailable"
            : command.RequesterIpAddress;
        var rateLimit = await rateLimiter.TryAcquireAsync(
            normalizedEmail,
            requesterIpAddress,
            cancellationToken);
        if (!rateLimit.IsAllowed)
        {
            throw new RateLimitExceededException(
                rateLimit.RetryAfter ?? policy.RateLimitWindow);
        }

        var challengeId = EntityId.New();
        var oneTimeCode = passwordGenerator.Generate(policy.CodeLength);
        var otpHash = passwordHasher.Hash(challengeId, oneTimeCode);
        var createdAt = clock.UtcNow;
        var challenge = EmailAuthenticationChallenge.Create(
            normalizedEmail,
            otpHash,
            createdAt.Add(policy.Lifetime),
            createdAt,
            challengeId);

        await challengeRepository.ReplacePendingAsync(challenge, cancellationToken);

        try
        {
            await emailSender.SendAsync(
                new AuthenticationEmailMessage(
                    normalizedEmail,
                    oneTimeCode,
                    challenge.ExpiresAt),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await challengeRepository.DeleteAsync(challenge.Id, CancellationToken.None);
            throw;
        }
        catch (Exception)
        {
            try
            {
                await challengeRepository.DeleteAsync(challenge.Id, CancellationToken.None);
            }
            catch (Exception)
            {
                throw new AuthenticationEmailDeliveryException();
            }

            throw new AuthenticationEmailDeliveryException();
        }

        return RequestEmailChallengeResult.Accepted;
    }
}

public sealed record RequestEmailChallengeCommand(
    string? Email,
    string? RequesterIpAddress);

public sealed record RequestEmailChallengeResult
{
    private RequestEmailChallengeResult()
    {
    }

    public static RequestEmailChallengeResult Accepted { get; } = new();
}
