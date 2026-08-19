namespace Beeexy.Application.Identity;

public sealed class AuthenticationEmailDeliveryException
    : Exception
{
    public AuthenticationEmailDeliveryException()
        : base("The authentication email could not be delivered.")
    {
    }
}
