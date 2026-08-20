namespace Beeexy.Application.Patients;

public sealed class AccountProfileInvariantException : Exception
{
    public AccountProfileInvariantException()
        : base("The current account profile state is inconsistent.")
    {
    }
}

public sealed class ProfileUpdateConcurrencyException : Exception
{
    public ProfileUpdateConcurrencyException()
        : base("The patient profile was changed by another request.")
    {
    }
}
