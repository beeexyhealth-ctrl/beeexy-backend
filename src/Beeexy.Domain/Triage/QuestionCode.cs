namespace Beeexy.Domain.Triage;

public sealed record QuestionCode
{
    public const int MaximumLength = 100;

    private QuestionCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static QuestionCode Create(string value)
    {
        return new QuestionCode(
            TriageValueGuard.RequiredIdentifier(value, MaximumLength, nameof(value)));
    }

    public override string ToString()
    {
        return Value;
    }
}
