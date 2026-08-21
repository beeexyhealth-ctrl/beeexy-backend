namespace Beeexy.Domain.Triage;

public sealed record ClinicalFindingInput(
    string FindingCode,
    string SourceRuleCode,
    string? MessageReference = null);
