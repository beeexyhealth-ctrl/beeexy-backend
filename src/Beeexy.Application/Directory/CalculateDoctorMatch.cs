using Beeexy.Application.Common;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;

namespace Beeexy.Application.Directory;

public sealed class CalculateDoctorMatch(
    IDoctorMatchingRepository repository,
    DeterministicDoctorMatchEngine engine)
{
    public async Task<CalculateDoctorMatchResult> ExecuteAsync(
        CalculateDoctorMatchQuery query,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(query, null, cancellationToken);
    }

    public async Task<CalculateDoctorMatchResult> ExecuteAsync(
        CalculateDoctorMatchQuery query,
        IReadOnlyCollection<EntityId>? candidateDoctorIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var version = DoctorDirectoryInputNormalizer.NormalizeRequiredCode(
            query.RuleVersion,
            "ruleVersion",
            "doctor_matching.rule_version_invalid");
        var criteria = DoctorDirectoryInputNormalizer.NormalizeFilter(
            query.SpecialtyCode,
            query.LanguageCode,
            query.Locality,
            query.AdministrativeArea,
            query.Country,
            query.InsurancePlanCode,
            "doctor_matching.criteria_invalid");

        var rule = await repository.GetRuleAsync(
            DirectoryCode.Create(version),
            cancellationToken) ?? throw new DoctorMatchRuleNotFoundException();
        var candidates = await repository.ListEligibleCandidatesAsync(
            candidateDoctorIds,
            cancellationToken);
        return engine.Calculate(rule, criteria, candidates);
    }
}

public sealed record CalculateDoctorMatchQuery(
    string? RuleVersion,
    string? SpecialtyCode = null,
    string? LanguageCode = null,
    string? Locality = null,
    string? AdministrativeArea = null,
    string? Country = null,
    string? InsurancePlanCode = null);

public sealed record CalculateDoctorMatchResult(
    DoctorMatchRuleIdentity Rule,
    DoctorDirectoryFilter Criteria,
    IReadOnlyList<DoctorMatchCandidateResult> Candidates);

public sealed record DoctorMatchRuleIdentity(
    string PackageCode,
    string Version,
    string ContentHash,
    string FormulaCode,
    string MissingInputCode,
    string TieBreakCode,
    int MaximumScorePoints);

public sealed record DoctorMatchCandidateResult(
    EntityId DoctorId,
    int TotalDemoMatchScorePoints,
    IReadOnlyList<DoctorMatchFactorResult> Factors);

public sealed record DoctorMatchFactorResult(
    string FactorCode,
    string SemanticsCode,
    int WeightPoints,
    DoctorMatchFactorState State,
    int ContributionPoints,
    string ExplanationCode,
    IReadOnlyList<DoctorMatchExplanationValue> ExplanationData);

public sealed record DoctorMatchExplanationValue(string Key, string Value);

public enum DoctorMatchFactorState
{
    Matched,
    NotMatched,
    NotApplicable
}

public sealed record DoctorMatchRuleDefinition(
    string PackageCode,
    string Version,
    string ContentHash,
    int SpecialtyWeightPoints,
    int LanguageWeightPoints,
    int LocationWeightPoints,
    int StoredInsuranceWeightPoints);

public sealed record DoctorMatchCandidateSnapshot(
    EntityId DoctorId,
    IReadOnlyList<string> SpecialtyCodes,
    IReadOnlyList<string> LanguageCodes,
    IReadOnlyList<DoctorMatchCandidateLocation> Locations,
    IReadOnlyList<string> StoredInsurancePlanCodes);

public sealed record DoctorMatchCandidateLocation(
    string Locality,
    string AdministrativeArea,
    string Country);

public interface IDoctorMatchingRepository
{
    Task<DoctorMatchRuleDefinition?> GetRuleAsync(
        DirectoryCode version,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DoctorMatchCandidateSnapshot>> ListEligibleCandidatesAsync(
        IReadOnlyCollection<EntityId>? doctorIds = null,
        CancellationToken cancellationToken = default);
}

public sealed class DoctorMatchRuleNotFoundException : Exception;

public sealed class DeterministicDoctorMatchEngine
{
    public CalculateDoctorMatchResult Calculate(
        DoctorMatchRuleDefinition rule,
        DoctorDirectoryFilter criteria,
        IReadOnlyList<DoctorMatchCandidateSnapshot> candidates)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(candidates);
        ValidateRule(rule);

        var results = candidates
            .Select(candidate => CalculateCandidate(rule, criteria, candidate))
            .OrderByDescending(candidate => candidate.TotalDemoMatchScorePoints)
            .ThenBy(
                candidate => candidate.DoctorId.Value.ToString("D"),
                StringComparer.Ordinal)
            .ToArray();

        return new CalculateDoctorMatchResult(
            new DoctorMatchRuleIdentity(
                rule.PackageCode,
                rule.Version,
                rule.ContentHash,
                DoctorMatchRuleSemantics.FormulaCode,
                DoctorMatchRuleSemantics.MissingInputCode,
                DoctorMatchRuleSemantics.TieBreakCode,
                DoctorMatchRuleConfiguration.MaximumScorePoints),
            criteria,
            results);
    }

    private static DoctorMatchCandidateResult CalculateCandidate(
        DoctorMatchRuleDefinition rule,
        DoctorDirectoryFilter criteria,
        DoctorMatchCandidateSnapshot candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var factors = new[]
        {
            EvaluateCodeFactor(
                DoctorMatchFactorCodes.Specialty,
                rule.SpecialtyWeightPoints,
                "specialtyCode",
                criteria.SpecialtyCode,
                candidate.SpecialtyCodes),
            EvaluateCodeFactor(
                DoctorMatchFactorCodes.Language,
                rule.LanguageWeightPoints,
                "languageCode",
                criteria.LanguageCode,
                candidate.LanguageCodes),
            EvaluateLocationFactor(rule.LocationWeightPoints, criteria, candidate.Locations),
            EvaluateCodeFactor(
                DoctorMatchFactorCodes.StoredInsurance,
                rule.StoredInsuranceWeightPoints,
                "insurancePlanCode",
                criteria.InsurancePlanCode,
                candidate.StoredInsurancePlanCodes)
        };

        return new DoctorMatchCandidateResult(
            candidate.DoctorId,
            factors.Sum(factor => factor.ContributionPoints),
            factors);
    }

    private static DoctorMatchFactorResult EvaluateCodeFactor(
        string factorCode,
        int weight,
        string explanationKey,
        string? criterion,
        IReadOnlyList<string> storedCodes)
    {
        ArgumentNullException.ThrowIfNull(storedCodes);
        if (criterion is null)
        {
            return Factor(factorCode, weight, DoctorMatchFactorState.NotApplicable, []);
        }

        var matched = storedCodes.Contains(criterion, StringComparer.Ordinal);
        return Factor(
            factorCode,
            weight,
            matched ? DoctorMatchFactorState.Matched : DoctorMatchFactorState.NotMatched,
            [new DoctorMatchExplanationValue(explanationKey, criterion)]);
    }

    private static DoctorMatchFactorResult EvaluateLocationFactor(
        int weight,
        DoctorDirectoryFilter criteria,
        IReadOnlyList<DoctorMatchCandidateLocation> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        if (criteria.Locality is null &&
            criteria.AdministrativeArea is null &&
            criteria.Country is null)
        {
            return Factor(
                DoctorMatchFactorCodes.Location,
                weight,
                DoctorMatchFactorState.NotApplicable,
                []);
        }

        var matched = locations.Any(location =>
            (criteria.Locality is null || location.Locality == criteria.Locality) &&
            (criteria.AdministrativeArea is null ||
                location.AdministrativeArea == criteria.AdministrativeArea) &&
            (criteria.Country is null || location.Country == criteria.Country));
        var data = new List<DoctorMatchExplanationValue>(3);
        AddIfPresent(data, "locality", criteria.Locality);
        AddIfPresent(data, "administrativeArea", criteria.AdministrativeArea);
        AddIfPresent(data, "country", criteria.Country);
        return Factor(
            DoctorMatchFactorCodes.Location,
            weight,
            matched ? DoctorMatchFactorState.Matched : DoctorMatchFactorState.NotMatched,
            data);
    }

    private static DoctorMatchFactorResult Factor(
        string factorCode,
        int weight,
        DoctorMatchFactorState state,
        IReadOnlyList<DoctorMatchExplanationValue> data)
    {
        var stateCode = state switch
        {
            DoctorMatchFactorState.Matched => "matched",
            DoctorMatchFactorState.NotMatched => "not_matched",
            DoctorMatchFactorState.NotApplicable => "not_applicable",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        return new DoctorMatchFactorResult(
            factorCode,
            DoctorMatchFactorSemanticsCodes.For(factorCode),
            weight,
            state,
            state == DoctorMatchFactorState.Matched ? weight : 0,
            $"demo_match.{factorCode}.{stateCode}",
            data);
    }

    private static void AddIfPresent(
        ICollection<DoctorMatchExplanationValue> values,
        string key,
        string? value)
    {
        if (value is not null)
        {
            values.Add(new DoctorMatchExplanationValue(key, value));
        }
    }

    private static void ValidateRule(DoctorMatchRuleDefinition rule)
    {
        var weights = new[]
        {
            rule.SpecialtyWeightPoints,
            rule.LanguageWeightPoints,
            rule.LocationWeightPoints,
            rule.StoredInsuranceWeightPoints
        };
        if (weights.Any(weight => weight is < 1 or > 100) ||
            weights.Sum() != DoctorMatchRuleConfiguration.MaximumScorePoints)
        {
            throw new InvalidOperationException(
                "The persisted demo matching rule has invalid weights.");
        }
    }
}
