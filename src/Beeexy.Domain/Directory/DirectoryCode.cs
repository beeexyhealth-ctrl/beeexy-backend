namespace Beeexy.Domain.Directory;

public sealed record DirectoryCode
{
    public const int MaximumLength = 100;

    private DirectoryCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static DirectoryCode Create(string value)
    {
        return new DirectoryCode(
            DirectoryValueGuard.RequiredIdentifier(value, MaximumLength, nameof(value)));
    }

    public override string ToString() => Value;
}
