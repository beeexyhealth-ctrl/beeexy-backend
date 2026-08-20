namespace Beeexy.Application.Identity;

public sealed class SessionAuthenticationException : Exception
{
    public SessionAuthenticationException()
        : base("The authentication session is invalid.")
    {
    }
}
