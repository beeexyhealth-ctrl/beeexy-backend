namespace Beeexy.Domain.Directory;

public sealed record DirectoryName
{
    public const int MaximumLength = 200;

    private DirectoryName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static DirectoryName Create(string value)
    {
        return new DirectoryName(
            DirectoryValueGuard.RequiredText(value, MaximumLength, nameof(value)));
    }

    public override string ToString() => Value;
}
