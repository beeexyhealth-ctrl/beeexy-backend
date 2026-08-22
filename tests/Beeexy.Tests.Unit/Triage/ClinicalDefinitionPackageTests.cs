using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Triage;

public sealed class ClinicalDefinitionPackageTests
{
    private readonly ClinicalDefinitionPackageValidator _validator = new();

    [Fact]
    public void PathwayIdentity_UsesStableMachineReadableCodes()
    {
        Assert.Equal("ABDOMINAL_PAIN", ClinicalPathways.AbdominalPain.Value);
        Assert.Equal(7, ClinicalPathways.Recognized.Count);
        Assert.Equal(
            [ClinicalPathways.Headache, ClinicalPathways.AbdominalPain, ClinicalPathways.Fever],
            ClinicalPathways.Supported);
        Assert.Equal(
            ClinicalPathways.AbdominalPain,
            ClinicalPathwayCode.Create("ABDOMINAL_PAIN"));
    }

    [Fact]
    public void ProvisionalPackage_HasStableVersionIdentityAndProvenance()
    {
        var package = CreatePackage();

        Assert.Equal(AbdominalPainProvisionalPackage.VersionIdentifier, package.Version.Value);
        Assert.Equal(ClinicalPathways.AbdominalPain, package.Pathway);
        Assert.Equal(
            ClinicalContentSource.ReferencePlatformDerived,
            package.ContentStatus.Source);
        Assert.Equal(ClinicalReviewStatus.Provisional, package.ContentStatus.ReviewStatus);
        Assert.Equal(
            ClinicalApprovalStatus.PendingFormalReview,
            package.ContentStatus.ApprovalStatus);
        Assert.Null(package.Questionnaire.ApprovedAt);
        Assert.NotNull(package.Questionnaire.ActivatedAt);
        Assert.Null(package.RuleSet.ApprovedAt);
        Assert.NotNull(package.RuleSet.ActivatedAt);
        _validator.Validate(package);
    }

    [Fact]
    public void Questionnaire_ContainsStableUniqueCodesAndRequiredAnswerTypes()
    {
        var package = CreatePackage();

        Assert.Equal(41, package.Questions.Count);
        Assert.Equal(41, package.Questions.Select(value => value.Code).Distinct().Count());
        Assert.Equal(
            Enum.GetValues<ClinicalAnswerType>().Order(),
            package.Questions.Select(value => value.Answer.Type).Distinct().Order());
        Assert.Contains(package.Questions, value => value.Code.Value == "MAIN_SYMPTOM");
        Assert.Contains(package.Questions, value => value.Code.Value == "ALLERGENS");
        Assert.All(package.Questions, value => Assert.DoesNotContain(' ', value.Code.Value));
    }

    [Fact]
    public void Branches_RepresentPriorityAndOnlyReferencePackageQuestions()
    {
        var package = CreatePackage();
        var questionCodes = package.Questions.Select(value => value.Code).ToHashSet();

        Assert.Contains(package.Branches, value => value.Code == "SUDDEN_ONSET_PRIORITY");
        Assert.Contains(package.Branches, value => value.Code == "LOWER_ABDOMINAL_DETAILS");
        Assert.Contains(package.Branches, value => value.Code == "VOMITING_DETAILS");
        Assert.Contains(package.Branches, value => value.Code == "BLOOD_SOURCE_DETAILS");
        Assert.Contains(package.Branches, value => value.Code == "URINARY_DETAILS");
        Assert.Contains(
            package.Branches,
            value => value.Priority == ClinicalQuestionPriority.RedFlagScreening);
        Assert.All(package.Branches, branch =>
        {
            Assert.Contains(branch.TriggerQuestionCode, questionCodes);
            Assert.All(branch.NextQuestionCodes, code => Assert.Contains(code, questionCodes));
        });
    }

    [Fact]
    public void Validator_RejectsUnknownBranchQuestionReference()
    {
        var package = CreatePackage();
        var invalidBranches = package.Branches.Concat(
        [
            new ClinicalBranchDefinition(
                "INVALID_REFERENCE",
                QuestionCode.Create("MAIN_SYMPTOM"),
                ClinicalConditionOperator.Equals,
                ["ABDOMINAL_PAIN"],
                [QuestionCode.Create("DOES_NOT_EXIST")],
                ClinicalQuestionPriority.Ordinary)
        ]).ToArray();
        var invalid = new ClinicalDefinitionPackage(
            package.Pathway,
            package.Questionnaire,
            package.RuleSet,
            package.Questions,
            invalidBranches,
            package.RuleDefinitions);

        Assert.Throws<ClinicalDefinitionValidationException>(() => _validator.Validate(invalid));
    }

    [Fact]
    public void Validator_RejectsBranchAnswerValueOutsideQuestionSchema()
    {
        var package = CreatePackage();
        var invalidBranches = package.Branches.Concat(
        [
            new ClinicalBranchDefinition(
                "INVALID_BOOLEAN_VALUE",
                QuestionCode.Create("HAS_FEVER"),
                ClinicalConditionOperator.Equals,
                ["MAYBE"],
                [QuestionCode.Create("MEASURED_TEMPERATURE_C")],
                ClinicalQuestionPriority.RedFlagScreening)
        ]).ToArray();
        var invalid = new ClinicalDefinitionPackage(
            package.Pathway,
            package.Questionnaire,
            package.RuleSet,
            package.Questions,
            invalidBranches,
            package.RuleDefinitions);

        Assert.Throws<ClinicalDefinitionValidationException>(() => _validator.Validate(invalid));
    }

    [Fact]
    public void Validator_RejectsUnknownRuleFactReference()
    {
        var package = CreatePackage();
        var invalidRules = package.RuleDefinitions.Rules.Concat(
        [
            new ClinicalRuleDefinition(
                "INVALID_FACT_REFERENCE",
                ClinicalUrgencies.Low,
                false,
                "Test-only malformed rule.",
                [
                    new ClinicalConditionDefinition(
                        QuestionCode.Create("DOES_NOT_EXIST"),
                        ClinicalConditionOperator.Equals,
                        "TRUE")
                ])
        ]).ToArray();
        var invalidRuleDefinitions = package.RuleDefinitions with { Rules = invalidRules };
        var invalid = new ClinicalDefinitionPackage(
            package.Pathway,
            package.Questionnaire,
            package.RuleSet,
            package.Questions,
            package.Branches,
            invalidRuleDefinitions);

        Assert.Throws<ClinicalDefinitionValidationException>(() => _validator.Validate(invalid));
    }

    [Fact]
    public void Validator_RejectsDuplicateQuestionCode()
    {
        var package = CreatePackage();
        var invalid = new ClinicalDefinitionPackage(
            package.Pathway,
            package.Questionnaire,
            package.RuleSet,
            package.Questions.Concat([package.Questions[0]]).ToArray(),
            package.Branches,
            package.RuleDefinitions);

        Assert.Throws<ClinicalDefinitionValidationException>(() => _validator.Validate(invalid));
    }

    [Fact]
    public void UrgencyVocabulary_HasDeterministicNonDowngradingOrderAndRecommendations()
    {
        var package = CreatePackage();

        Assert.Equal(
            ["VERY_LOW", "LOW", "MEDIUM", "HIGH", "CRITICAL"],
            package.RuleDefinitions.Urgencies
                .OrderBy(value => value.SeverityRank)
                .Select(value => value.Code.Value));
        Assert.All(
            package.RuleDefinitions.Dispositions,
            value => Assert.False(string.IsNullOrWhiteSpace(value.Recommendation)));
        Assert.Equal(5, package.RuleDefinitions.Dispositions.Count);
        Assert.Equal(4, ClinicalUrgencies.SeverityOrder[ClinicalUrgencies.Critical]);
    }

    [Fact]
    public void RuleArtifacts_ContainOnlyDocumentedAbdominalConcepts()
    {
        var package = CreatePackage();
        var rules = package.RuleDefinitions.Rules;

        Assert.Equal(10, rules.Count);
        Assert.Contains(rules, value =>
            value.Code == "MEDIUM_ABDOMINAL_PAIN_WITH_TEMPERATURE_AT_LEAST_38_C" &&
            value.AllOf.Any(condition => condition.ExpectedValue == "38"));
        Assert.Contains(rules, value => value.Code == "MEDIUM_PERSISTENT_VOMITING");
        Assert.Contains(rules, value =>
            value.Code == "MEDIUM_INABILITY_TO_KEEP_FLUIDS_DOWN");
        Assert.Contains(rules, value =>
            value.Code == "HIGH_BLOOD_IN_VOMIT_STOOL_OR_BLACK_TARRY_STOOL");
        Assert.Contains(rules, value =>
            value.Code == "HIGH_ABDOMINAL_PAIN_WITH_SHORTNESS_OF_BREATH");
        Assert.Contains(rules, value => value.Code == "LOW_PERSISTING_WITHOUT_IMPROVEMENT");
        Assert.Contains(rules, value =>
            value.Code == "VERY_LOW_STABLE_OR_IMPROVING_WITH_ORAL_INTAKE" &&
            value.RequiresNoIdentifiedRedFlags);
        Assert.Equal(13, package.RuleDefinitions.RedFlags.Count);
    }

    [Fact]
    public void ProvisionalPackage_ContainsNoFabricatedCriticalTrigger()
    {
        var package = CreatePackage();

        Assert.DoesNotContain(
            package.RuleDefinitions.Rules,
            value => value.MinimumUrgency == ClinicalUrgencies.Critical);
        Assert.Contains(package.RuleDefinitions.ClinicalLimitations, value =>
            value.Contains("no CRITICAL trigger", StringComparison.Ordinal));
    }

    [Fact]
    public void VersionEntities_AreImmutableAfterImport()
    {
        Assert.Empty(PublicMutationMethods(typeof(QuestionnaireDefinitionVersion)));
        Assert.Empty(PublicMutationMethods(typeof(ClinicalRuleSetVersion)));
        Assert.All(
            typeof(QuestionnaireDefinitionVersion).GetProperties(),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
        Assert.All(
            typeof(ClinicalRuleSetVersion).GetProperties(),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }

    [Fact]
    public void ClinicalDefinitions_ExposeNoDiseaseProbabilityField()
    {
        var types = new[]
        {
            typeof(ClinicalDefinitionPackage),
            typeof(ClinicalRulePackageDefinition),
            typeof(ClinicalRuleDefinition),
            typeof(ClinicalRedFlagDefinition),
            typeof(ClinicalConditionDefinition)
        };

        Assert.All(types, type => Assert.DoesNotContain(
            type.GetProperties(),
            property => property.Name.Contains("Probability", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Registry_DistinguishesSupportedUnsupportedAndUnknownWithoutCrossMapping()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.AbdominalPain);
        var registry = new ClinicalPathwayRegistry(new StubDefinitionProvider(package));
        var supported = await registry.ResolveAsync("ABDOMINAL_PAIN");

        Assert.Equal(ClinicalPathwayResolutionStatus.Supported, supported.Status);
        Assert.Same(package, supported.ActiveDefinition);

        foreach (var unsupported in ClinicalPathways.Recognized.Except(
            ClinicalPathways.Supported))
        {
            var resolution = await registry.ResolveAsync(unsupported.Value);
            Assert.Equal(
                ClinicalPathwayResolutionStatus.RecognizedButUnsupported,
                resolution.Status);
            Assert.True(resolution.IsRecognized);
            Assert.False(resolution.IsSupported);
            Assert.Null(resolution.ActiveDefinition);
        }

        var unknown = await registry.ResolveAsync("NOT_A_CLINICAL_PATHWAY");
        Assert.Equal(ClinicalPathwayResolutionStatus.Unknown, unknown.Status);
        Assert.Null(unknown.Pathway);
        Assert.Null(unknown.ActiveDefinition);
    }

    private static IEnumerable<string> PublicMutationMethods(Type type)
    {
        return type.GetMethods()
            .Where(method =>
                method.DeclaringType == type &&
                !method.IsStatic &&
                !method.IsSpecialName)
            .Select(method => method.Name);
    }

    private static ClinicalDefinitionPackage CreatePackage()
    {
        return AbdominalPainProvisionalPackage.Create();
    }

    private sealed class StubDefinitionProvider(ClinicalDefinitionPackage package)
        : IClinicalDefinitionProvider
    {
        public Task<ClinicalDefinitionPackage?> GetActiveDefinitionAsync(
            ClinicalPathwayCode pathway,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ClinicalDefinitionPackage?>(
                pathway == package.Pathway ? package : null);
        }

        public Task<ClinicalDefinitionPackage?> GetDefinitionAsync(
            ClinicalPathwayCode pathway,
            DefinitionVersion version,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ClinicalDefinitionPackage?>(
                pathway == package.Pathway && version == package.Version ? package : null);
        }
    }
}
