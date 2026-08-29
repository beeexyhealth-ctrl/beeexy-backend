using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Infrastructure.DirectoryServices;

namespace Beeexy.Tests.Unit.DoctorMatching;

public sealed class DoctorMatchRulePackageTests
{
    private readonly DoctorMatchRulePackageValidator _validator = new();

    [Fact]
    public void ApprovedPackage_HasExactImmutableIdentitySemanticsAndEqualIntegerWeights()
    {
        var first = ProductApprovedDemoDoctorMatchRule.Create();
        var second = ProductApprovedDemoDoctorMatchRule.Create();

        _validator.Validate(first);
        Assert.Equal(ProductApprovedDemoDoctorMatchRule.PackageCode, first.PackageCode.Value);
        Assert.Equal(ProductApprovedDemoDoctorMatchRule.Version, first.Version.Value);
        Assert.Equal(ProductApprovedDemoDoctorMatchRule.ExpectedContentHash, first.ContentHash);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(DoctorMatchFactorCodes.Ordered, first.Factors.Select(value => value.Code));
        Assert.Equal(
            DoctorMatchFactorCodes.Ordered.Select(DoctorMatchFactorSemanticsCodes.For),
            first.Factors.Select(value => value.SemanticsCode));
        Assert.All(first.Factors, factor => Assert.Equal(25, factor.WeightPoints));
        Assert.Equal(100, first.Factors.Sum(factor => factor.WeightPoints));
        Assert.Equal("sum_matched_weight_points_no_reweight_v1", DoctorMatchRuleSemantics.FormulaCode);
        Assert.Equal(
            "not_applicable_zero_contribution_v1",
            DoctorMatchRuleSemantics.MissingInputCode);
        Assert.Equal("score_desc_uuid_text_asc_v1", DoctorMatchRuleSemantics.TieBreakCode);
    }

    [Fact]
    public void ChangedConfiguration_ProducesDifferentContentHashForSameVersion()
    {
        var approved = ProductApprovedDemoDoctorMatchRule.Create();
        var changed = CreatePackage(
        [
            Factor(DoctorMatchFactorCodes.Specialty, 40),
            Factor(DoctorMatchFactorCodes.Language, 20),
            Factor(DoctorMatchFactorCodes.Location, 20),
            Factor(DoctorMatchFactorCodes.StoredInsurance, 20)
        ]);

        _validator.Validate(changed);
        Assert.Equal(approved.Version, changed.Version);
        Assert.NotEqual(approved.ContentHash, changed.ContentHash);
    }

    [Theory]
    [MemberData(nameof(InvalidFactors))]
    public void Validator_RejectsDuplicateUnknownMissingAndInvalidWeights(
        DoctorMatchRuleFactorDefinition[] factors)
    {
        var package = CreatePackage(factors);

        Assert.Throws<DoctorMatchRuleValidationException>(() => _validator.Validate(package));
    }

    [Fact]
    public void PersistedConfiguration_RequiresLowercaseHashAndWeightsTotalingOneHundred()
    {
        var id = EntityId.New();
        var packageCode = DirectoryCode.Create("demo-match-package");

        Assert.Throws<ArgumentException>(() => DoctorMatchRuleConfiguration.Create(
            id,
            packageCode,
            new string('A', 64),
            25,
            25,
            25,
            25));
        Assert.Throws<ArgumentException>(() => DoctorMatchRuleConfiguration.Create(
            id,
            packageCode,
            new string('a', 64),
            25,
            25,
            25,
            24));
    }

    public static TheoryData<DoctorMatchRuleFactorDefinition[]> InvalidFactors => new()
    {
        {
            [
                Factor(DoctorMatchFactorCodes.Specialty, 25),
                Factor(DoctorMatchFactorCodes.Specialty, 25),
                Factor(DoctorMatchFactorCodes.Location, 25),
                Factor(DoctorMatchFactorCodes.StoredInsurance, 25)
            ]
        },
        {
            [
                Factor(DoctorMatchFactorCodes.Specialty, 25),
                new(DoctorMatchFactorCodes.Language, "unknown_semantics", 25),
                Factor(DoctorMatchFactorCodes.Location, 25),
                Factor(DoctorMatchFactorCodes.StoredInsurance, 25)
            ]
        },
        {
            [
                Factor(DoctorMatchFactorCodes.Specialty, 25),
                new("popularity", "unsupported_semantics", 25),
                Factor(DoctorMatchFactorCodes.Location, 25),
                Factor(DoctorMatchFactorCodes.StoredInsurance, 25)
            ]
        },
        {
            [
                Factor(DoctorMatchFactorCodes.Specialty, 34),
                Factor(DoctorMatchFactorCodes.Language, 33),
                Factor(DoctorMatchFactorCodes.Location, 33)
            ]
        },
        {
            [
                Factor(DoctorMatchFactorCodes.Specialty, 0),
                Factor(DoctorMatchFactorCodes.Language, 30),
                Factor(DoctorMatchFactorCodes.Location, 30),
                Factor(DoctorMatchFactorCodes.StoredInsurance, 40)
            ]
        },
        {
            [
                Factor(DoctorMatchFactorCodes.Specialty, 25),
                Factor(DoctorMatchFactorCodes.Language, 25),
                Factor(DoctorMatchFactorCodes.Location, 25),
                Factor(DoctorMatchFactorCodes.StoredInsurance, 24)
            ]
        }
    };

    private static DoctorMatchRulePackage CreatePackage(
        IEnumerable<DoctorMatchRuleFactorDefinition> factors) =>
        DoctorMatchRulePackage.Create(
            DirectoryCode.Create(ProductApprovedDemoDoctorMatchRule.PackageCode),
            DirectoryCode.Create(ProductApprovedDemoDoctorMatchRule.Version),
            new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero),
            factors);

    private static DoctorMatchRuleFactorDefinition Factor(string code, int weight) =>
        new(code, DoctorMatchFactorSemanticsCodes.For(code), weight);
}
