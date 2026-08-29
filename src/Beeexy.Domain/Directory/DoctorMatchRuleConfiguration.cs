using Beeexy.Domain.Common;

namespace Beeexy.Domain.Directory;

public sealed class DoctorMatchRuleConfiguration
{
    public const int MaximumScorePoints = 100;

    private DoctorMatchRuleConfiguration()
    {
        PackageCode = null!;
        ContentHash = null!;
    }

    private DoctorMatchRuleConfiguration(
        EntityId ruleVersionId,
        DirectoryCode packageCode,
        string contentHash,
        int specialtyWeightPoints,
        int languageWeightPoints,
        int locationWeightPoints,
        int storedInsuranceWeightPoints)
    {
        RuleVersionId = ruleVersionId;
        PackageCode = packageCode;
        ContentHash = contentHash;
        SpecialtyWeightPoints = specialtyWeightPoints;
        LanguageWeightPoints = languageWeightPoints;
        LocationWeightPoints = locationWeightPoints;
        StoredInsuranceWeightPoints = storedInsuranceWeightPoints;
    }

    public EntityId RuleVersionId { get; private set; }

    public DirectoryCode PackageCode { get; private set; }

    public string ContentHash { get; private set; }

    public int SpecialtyWeightPoints { get; private set; }

    public int LanguageWeightPoints { get; private set; }

    public int LocationWeightPoints { get; private set; }

    public int StoredInsuranceWeightPoints { get; private set; }

    public static DoctorMatchRuleConfiguration Create(
        EntityId ruleVersionId,
        DirectoryCode packageCode,
        string contentHash,
        int specialtyWeightPoints,
        int languageWeightPoints,
        int locationWeightPoints,
        int storedInsuranceWeightPoints)
    {
        DirectoryValueGuard.EnsureNonEmpty(ruleVersionId, nameof(ruleVersionId));
        ArgumentNullException.ThrowIfNull(packageCode);
        if (contentHash is null ||
            contentHash.Length != 64 ||
            contentHash.Any(character =>
                !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "The matching-rule content hash must be 64 lowercase hexadecimal characters.",
                nameof(contentHash));
        }

        var weights = new[]
        {
            specialtyWeightPoints,
            languageWeightPoints,
            locationWeightPoints,
            storedInsuranceWeightPoints
        };
        if (weights.Any(weight => weight is < 1 or > MaximumScorePoints) ||
            weights.Sum() != MaximumScorePoints)
        {
            throw new ArgumentException(
                $"Matching-rule weights must be positive integers totaling {MaximumScorePoints}.",
                nameof(specialtyWeightPoints));
        }

        return new DoctorMatchRuleConfiguration(
            ruleVersionId,
            packageCode,
            contentHash,
            specialtyWeightPoints,
            languageWeightPoints,
            locationWeightPoints,
            storedInsuranceWeightPoints);
    }
}
