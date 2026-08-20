namespace Beeexy.Application.Identity;

public sealed class EmailChallengeUnauthorizedException : Exception
{
    public EmailChallengeUnauthorizedException()
        : base("The email challenge could not be verified.")
    {
    }
}

public sealed class EmailChallengeReplayException : Exception
{
    public EmailChallengeReplayException()
        : base("The email challenge has already been used.")
    {
    }
}

public sealed class EmailChallengeAttemptLimitException : Exception
{
    public EmailChallengeAttemptLimitException()
        : base("The email challenge attempt limit has been reached.")
    {
    }
}

public sealed class AccountAuthenticationRejectedException : Exception
{
    public AccountAuthenticationRejectedException()
        : base("The account cannot authenticate.")
    {
    }
}

public sealed class IdentityProvisioningInvariantException : Exception
{
    public IdentityProvisioningInvariantException()
        : base("The account identity is internally inconsistent.")
    {
    }
}
