namespace Beeexy.Domain.Triage;

public sealed record QuestionnaireCode
{
    public const int MaximumLength = 100;

    private QuestionnaireCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static QuestionnaireCode Create(string value)
    {
        return new QuestionnaireCode(
            TriageValueGuard.RequiredIdentifier(value, MaximumLength, nameof(value)));
    }

    public override string ToString()
    {
        return Value;
    }
}
