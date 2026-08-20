using Beeexy.Application.Common;
using Beeexy.Domain.Common;
using Beeexy.Domain.Identity;

namespace Beeexy.Application.Identity;

public sealed class VerifyEmailChallenge(
    IClock clock,
    EmailChallengePolicy policy,
    IOneTimePasswordHasher passwordHasher,
    IEmailAuthenticationChallengeRepository challengeRepository,
    IIdentityVerificationTransaction transaction,
    ProvisionAccountAndPrimaryProfile provisionAccount,
    IssueAuthenticationTokens tokenIssuer)
{
    public async Task<VerifyEmailChallengeResult> ExecuteAsync(
        VerifyEmailChallengeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var email = ParseEmail(command.Email);
        var code = ValidateCode(command.Code);
        var now = clock.UtcNow;

        await transaction.BeginAsync(cancellationToken);
        var challenge = await challengeRepository.FindLatestForUpdateAsync(
            email,
            cancellationToken);

        if (challenge is null)
        {
            throw new EmailChallengeUnauthorizedException();
        }

        if (challenge.Status == ChallengeStatus.Consumed)
        {
            throw new EmailChallengeReplayException();
        }

        if (challenge.Status == ChallengeStatus.Expired)
        {
            throw new EmailChallengeUnauthorizedException();
        }

        if (challenge.IsExpiredAt(now))
        {
            challenge.MarkExpired(now);
            await transaction.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new EmailChallengeUnauthorizedException();
        }

        if (challenge.AttemptCount >= policy.MaximumVerificationAttempts)
        {
            throw new EmailChallengeAttemptLimitException();
        }

        if (!passwordHasher.Verify(challenge.Id, code, challenge.OtpHash))
        {
            challenge.RecordFailedAttempt(now);
            await transaction.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new EmailChallengeUnauthorizedException();
        }

        ProvisionedAccountResult identity;
        try
        {
            identity = await provisionAccount.ExecuteAsync(email, now, cancellationToken);
        }
        catch (AccountAuthenticationRejectedException)
        {
            // A valid one-time code for a disabled account is still consumed, preventing
            // repeated attempts while returning the same generic authentication failure.
            challenge.Consume(now);
            await transaction.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new EmailChallengeUnauthorizedException();
        }

        var authenticationSession = tokenIssuer.Execute(identity.Account.Id, now);
        challenge.Consume(now);
        await transaction.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new VerifyEmailChallengeResult(
            authenticationSession.Tokens,
            identity.Account.Id,
            identity.PrimaryProfile.Id,
            identity.PrimaryProfile.BeeexyId.Value);
    }

    private static NormalizedEmail ParseEmail(string? value)
    {
        try
        {
            return NormalizedEmail.Create(value ?? string.Empty);
        }
        catch (ArgumentException)
        {
            throw new RequestValidationException(
                "authentication.invalid_email",
                "A valid email address is required.");
        }
    }

    private string ValidateCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != policy.CodeLength ||
            value.Any(character => character is < '0' or > '9'))
        {
            throw new RequestValidationException(
                "authentication.invalid_code",
                "A valid verification code is required.");
        }

        return value;
    }
}

public sealed record VerifyEmailChallengeCommand(string? Email, string? Code);

public sealed record VerifyEmailChallengeResult(
    AuthenticationTokenPair Tokens,
    EntityId AccountId,
    EntityId ProfileId,
    string BeeexyId);
