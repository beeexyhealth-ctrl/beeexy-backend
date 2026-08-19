namespace Beeexy.Domain.Common;

public sealed class DomainException : Exception
{
    public DomainException(DomainError error)
        : base((error ?? throw new ArgumentNullException(nameof(error))).Message)
    {
        Error = error;
    }

    public DomainError Error { get; }
}
