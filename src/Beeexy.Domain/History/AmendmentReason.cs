namespace Beeexy.Domain.History;

public sealed record AmendmentReason
{
    private AmendmentReason(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static AmendmentReason Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An amendment reason is required.", nameof(value));
        }

        return new AmendmentReason(value.Trim());
    }

    public override string ToString()
    {
        return Value;
    }
}
