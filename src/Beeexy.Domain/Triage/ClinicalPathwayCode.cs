namespace Beeexy.Domain.Triage;

public sealed record ClinicalPathwayCode
{
    public const int MaximumLength = 100;

    private ClinicalPathwayCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ClinicalPathwayCode Create(string value)
    {
        return new ClinicalPathwayCode(
            TriageValueGuard.RequiredIdentifier(value, MaximumLength, nameof(value)));
    }

    public override string ToString()
    {
        return Value;
    }
}
