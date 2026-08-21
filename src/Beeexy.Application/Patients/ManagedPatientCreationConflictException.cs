namespace Beeexy.Application.Patients;

public sealed class ManagedPatientCreationConflictException : Exception
{
    public ManagedPatientCreationConflictException()
        : base("The managed patient and care relationship could not be created uniquely.")
    {
    }
}
