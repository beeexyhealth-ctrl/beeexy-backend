using System.Text.Json;
using Beeexy.Application.Common;
using Beeexy.Application.Directory;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Infrastructure.DirectoryServices;

namespace Beeexy.Tests.Unit.DoctorMatching;

public sealed class CalculateDoctorMatchTests
{
    private static readonly EntityId FirstId =
        EntityId.From(Guid.Parse("00000000-0000-4000-8000-000000000001"));
    private static readonly EntityId SecondId =
        EntityId.From(Guid.Parse("00000000-0000-4000-8000-000000000002"));

    [Fact]
    public async Task AllFactorsMatched_ReturnsExactHundredPointStructuredExplanation()
    {
        var useCase = UseCase(Candidate(
            FirstId,
            specialties: ["specialty-a"],
            languages: ["language-a"],
            locations: [new("Locality A", "Area A", "Country A")],
            insurance: ["plan-a"]));

        var result = await useCase.ExecuteAsync(new CalculateDoctorMatchQuery(
            ProductApprovedDemoDoctorMatchRule.Version,
            "specialty-a",
            "language-a",
            "Locality A",
            "Area A",
            "Country A",
            "plan-a"));

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(100, candidate.TotalDemoMatchScorePoints);
        Assert.Equal(DoctorMatchFactorCodes.Ordered, candidate.Factors.Select(value => value.FactorCode));
        Assert.All(candidate.Factors, factor =>
        {
            Assert.Equal(
                DoctorMatchFactorSemanticsCodes.For(factor.FactorCode),
                factor.SemanticsCode);
            Assert.Equal(25, factor.WeightPoints);
            Assert.Equal(DoctorMatchFactorState.Matched, factor.State);
            Assert.Equal(25, factor.ContributionPoints);
            Assert.EndsWith(".matched", factor.ExplanationCode, StringComparison.Ordinal);
        });
        AssertRuleIdentity(result.Rule);
    }

    [Fact]
    public async Task PartialAndAbsentInputs_UseNoReweightingAndExactStates()
    {
        var useCase = UseCase(Candidate(
            FirstId,
            specialties: ["specialty-a"],
            languages: ["language-b"],
            locations: [],
            insurance: ["plan-a"]));

        var result = await useCase.ExecuteAsync(new CalculateDoctorMatchQuery(
            ProductApprovedDemoDoctorMatchRule.Version,
            SpecialtyCode: "specialty-a",
            LanguageCode: "language-a"));

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(25, candidate.TotalDemoMatchScorePoints);
        Assert.Equal(
            [
                DoctorMatchFactorState.Matched,
                DoctorMatchFactorState.NotMatched,
                DoctorMatchFactorState.NotApplicable,
                DoctorMatchFactorState.NotApplicable
            ],
            candidate.Factors.Select(value => value.State));
        Assert.Equal([25, 0, 0, 0], candidate.Factors.Select(value => value.ContributionPoints));
    }

    [Fact]
    public async Task AbsentCriteria_MakesEveryFactorNotApplicableAndScoreZero()
    {
        var result = await UseCase(Candidate(FirstId)).ExecuteAsync(
            new CalculateDoctorMatchQuery(ProductApprovedDemoDoctorMatchRule.Version));

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(0, candidate.TotalDemoMatchScorePoints);
        Assert.All(candidate.Factors, factor =>
        {
            Assert.Equal(DoctorMatchFactorState.NotApplicable, factor.State);
            Assert.Equal(0, factor.ContributionPoints);
            Assert.Empty(factor.ExplanationData);
        });
    }

    [Fact]
    public async Task LocationFields_MustMatchTheSameEligibleStoredLocation()
    {
        var useCase = UseCase(Candidate(
            FirstId,
            locations:
            [
                new("Locality A", "Area A", "Country A"),
                new("Locality B", "Area B", "Country B")
            ]));

        var result = await useCase.ExecuteAsync(new CalculateDoctorMatchQuery(
            ProductApprovedDemoDoctorMatchRule.Version,
            Locality: "Locality A",
            AdministrativeArea: "Area B",
            Country: "Country A"));

        var location = Assert.Single(result.Candidates).Factors[2];
        Assert.Equal(DoctorMatchFactorState.NotMatched, location.State);
        Assert.Equal(0, location.ContributionPoints);
        Assert.Equal(
            ["locality", "administrativeArea", "country"],
            location.ExplanationData.Select(value => value.Key));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EachFactor_HasExactMatchedNotMatchedAndNotApplicableBehavior(int factorIndex)
    {
        var useCase = UseCase(Candidate(
            FirstId,
            specialties: ["specialty-a"],
            languages: ["language-a"],
            locations: [new("Locality A", "Area A", "Country A")],
            insurance: ["plan-a"]));
        var matchedQuery = factorIndex switch
        {
            0 => new CalculateDoctorMatchQuery(
                ProductApprovedDemoDoctorMatchRule.Version,
                SpecialtyCode: "specialty-a"),
            1 => new CalculateDoctorMatchQuery(
                ProductApprovedDemoDoctorMatchRule.Version,
                LanguageCode: "language-a"),
            2 => new CalculateDoctorMatchQuery(
                ProductApprovedDemoDoctorMatchRule.Version,
                Locality: "Locality A"),
            3 => new CalculateDoctorMatchQuery(
                ProductApprovedDemoDoctorMatchRule.Version,
                InsurancePlanCode: "plan-a"),
            _ => throw new ArgumentOutOfRangeException(nameof(factorIndex))
        };
        var notMatchedQuery = factorIndex switch
        {
            0 => matchedQuery with { SpecialtyCode = "specialty-b" },
            1 => matchedQuery with { LanguageCode = "language-b" },
            2 => matchedQuery with { Locality = "Locality B" },
            3 => matchedQuery with { InsurancePlanCode = "plan-b" },
            _ => throw new ArgumentOutOfRangeException(nameof(factorIndex))
        };

        var matched = Assert.Single((await useCase.ExecuteAsync(matchedQuery)).Candidates);
        var notMatched = Assert.Single((await useCase.ExecuteAsync(notMatchedQuery)).Candidates);

        Assert.Equal(DoctorMatchFactorState.Matched, matched.Factors[factorIndex].State);
        Assert.Equal(25, matched.Factors[factorIndex].ContributionPoints);
        Assert.EndsWith(
            ".matched",
            matched.Factors[factorIndex].ExplanationCode,
            StringComparison.Ordinal);
        Assert.Equal(DoctorMatchFactorState.NotMatched, notMatched.Factors[factorIndex].State);
        Assert.Equal(0, notMatched.Factors[factorIndex].ContributionPoints);
        Assert.EndsWith(
            ".not_matched",
            notMatched.Factors[factorIndex].ExplanationCode,
            StringComparison.Ordinal);
        Assert.All(
            matched.Factors.Where((_, index) => index != factorIndex),
            factor => Assert.Equal(DoctorMatchFactorState.NotApplicable, factor.State));
    }

    [Fact]
    public async Task TrueScoreTie_UsesCanonicalUuidTextAndIgnoresInputOrder()
    {
        var first = await UseCase(Candidate(SecondId), Candidate(FirstId)).ExecuteAsync(
            new CalculateDoctorMatchQuery(ProductApprovedDemoDoctorMatchRule.Version));
        var second = await UseCase(Candidate(FirstId), Candidate(SecondId)).ExecuteAsync(
            new CalculateDoctorMatchQuery(ProductApprovedDemoDoctorMatchRule.Version));

        Assert.Equal([FirstId, SecondId], first.Candidates.Select(value => value.DoctorId));
        Assert.Equal(
            JsonSerializer.Serialize(first),
            JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task RepeatedCalculation_IsByteForByteStableWithoutTimeRandomOrAuditFields()
    {
        var useCase = UseCase(Candidate(
            FirstId,
            specialties: ["specialty-a"],
            insurance: ["plan-a"]));
        var query = new CalculateDoctorMatchQuery(
            ProductApprovedDemoDoctorMatchRule.Version,
            SpecialtyCode: "specialty-a",
            InsurancePlanCode: "plan-a");

        var serialized = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            serialized.Add(JsonSerializer.Serialize(await useCase.ExecuteAsync(query)));
        }

        Assert.Single(serialized.Distinct(StringComparer.Ordinal));
        Assert.DoesNotContain("timestamp", serialized[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("patient", serialized[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnosis", serialized[0], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, "doctor_matching.rule_version_invalid")]
    [InlineData(" ", "doctor_matching.rule_version_invalid")]
    [InlineData("valid-version", null)]
    public async Task InputValidationAndUnknownVersion_AreDeterministic(
        string? version,
        string? expectedValidationCode)
    {
        var useCase = new CalculateDoctorMatch(
            new FakeRepository(null, []),
            new DeterministicDoctorMatchEngine());
        if (expectedValidationCode is not null)
        {
            var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
                useCase.ExecuteAsync(new CalculateDoctorMatchQuery(version)));
            Assert.Equal(expectedValidationCode, exception.Code);
            return;
        }

        await Assert.ThrowsAsync<DoctorMatchRuleNotFoundException>(() =>
            useCase.ExecuteAsync(new CalculateDoctorMatchQuery(version)));
    }

    [Fact]
    public async Task InvalidCriteria_IsRejectedBeforeRepositoryAccess()
    {
        var repository = new FakeRepository(Rule(), []);
        var useCase = new CalculateDoctorMatch(repository, new DeterministicDoctorMatchEngine());

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            useCase.ExecuteAsync(new CalculateDoctorMatchQuery(
                ProductApprovedDemoDoctorMatchRule.Version,
                SpecialtyCode: "not a code")));

        Assert.Equal("doctor_matching.criteria_invalid", exception.Code);
        Assert.Equal(0, repository.RuleReads);
    }

    private static CalculateDoctorMatch UseCase(params DoctorMatchCandidateSnapshot[] candidates) =>
        new(new FakeRepository(Rule(), candidates), new DeterministicDoctorMatchEngine());

    private static DoctorMatchRuleDefinition Rule() => new(
        ProductApprovedDemoDoctorMatchRule.PackageCode,
        ProductApprovedDemoDoctorMatchRule.Version,
        ProductApprovedDemoDoctorMatchRule.ExpectedContentHash,
        25,
        25,
        25,
        25);

    private static DoctorMatchCandidateSnapshot Candidate(
        EntityId id,
        IReadOnlyList<string>? specialties = null,
        IReadOnlyList<string>? languages = null,
        IReadOnlyList<DoctorMatchCandidateLocation>? locations = null,
        IReadOnlyList<string>? insurance = null) =>
        new(id, specialties ?? [], languages ?? [], locations ?? [], insurance ?? []);

    private static void AssertRuleIdentity(DoctorMatchRuleIdentity rule)
    {
        Assert.Equal(ProductApprovedDemoDoctorMatchRule.PackageCode, rule.PackageCode);
        Assert.Equal(ProductApprovedDemoDoctorMatchRule.Version, rule.Version);
        Assert.Equal(ProductApprovedDemoDoctorMatchRule.ExpectedContentHash, rule.ContentHash);
        Assert.Equal(100, rule.MaximumScorePoints);
        Assert.Equal(DoctorMatchRuleSemantics.FormulaCode, rule.FormulaCode);
        Assert.Equal(DoctorMatchRuleSemantics.MissingInputCode, rule.MissingInputCode);
        Assert.Equal(DoctorMatchRuleSemantics.TieBreakCode, rule.TieBreakCode);
    }

    private sealed class FakeRepository(
        DoctorMatchRuleDefinition? rule,
        IReadOnlyList<DoctorMatchCandidateSnapshot> candidates) : IDoctorMatchingRepository
    {
        public int RuleReads { get; private set; }

        public Task<DoctorMatchRuleDefinition?> GetRuleAsync(
            DirectoryCode version,
            CancellationToken cancellationToken = default)
        {
            RuleReads++;
            return Task.FromResult(rule is not null && rule.Version == version.Value ? rule : null);
        }

        public Task<IReadOnlyList<DoctorMatchCandidateSnapshot>> ListEligibleCandidatesAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(candidates);
    }
}
