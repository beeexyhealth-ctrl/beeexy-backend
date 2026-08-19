namespace Beeexy.Domain.Common;

public sealed record DomainError
{
    public DomainError(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Message = message;
    }

    public string Code { get; }

    public string Message { get; }
}
