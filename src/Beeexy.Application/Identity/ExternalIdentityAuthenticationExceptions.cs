namespace Beeexy.Application.Identity;

public sealed class ExternalIdentityAuthenticationException : Exception
{
    public ExternalIdentityAuthenticationException()
        : base("The external identity could not be authenticated.")
    {
    }
}

public sealed class ExternalIdentityProviderUnavailableException : Exception
{
    public ExternalIdentityProviderUnavailableException()
        : base("The external identity provider is unavailable.")
    {
    }
}
