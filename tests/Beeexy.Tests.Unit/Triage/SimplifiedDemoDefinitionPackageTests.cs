using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Triage;

public sealed class SimplifiedDemoDefinitionPackageTests
{
    private readonly ClinicalDefinitionPackageValidator _validator = new();

    [Fact]
    public void CreateAll_ContainsExactlyTheConfirmedPathways()
    {
        var packages = SimplifiedDemoDefinitionPackages.CreateAll();

        Assert.Equal(3, packages.Count);
        Assert.Equal(
            ["HEADACHE", "ABDOMINAL_PAIN", "FEVER"],
            packages.Select(value => value.Pathway.Value));
        Assert.Equal(3, packages.Select(value => value.Questionnaire.Id).Distinct().Count());
        Assert.Equal(3, packages.Select(value => value.RuleSet.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("HEADACHE", "Headache", 3)]
    [InlineData("ABDOMINAL_PAIN", "Stomach pain", 3)]
    [InlineData("FEVER", "Fever", 2)]
    public void Package_HasExactSimplifiedSchemaAndTruthfulProvenance(
        string pathwayCode,
        string displayLabel,
        int applicableAdditionalCount)
    {
        var package = Create(pathwayCode);

        _validator.Validate(package);
        Assert.Equal(ClinicalDefinitionPackageProfile.SimplifiedDemoIntake, package.Profile);
        Assert.Equal(ClinicalContentStatus.NonClinicalDemo, package.ContentStatus);
        Assert.Null(package.Questionnaire.ApprovedAt);
        Assert.Null(package.RuleSet.ApprovedAt);
        Assert.Equal(SimplifiedDemoDefinitionPackages.SourceReference,
            package.Questionnaire.SourceReference);
        Assert.Equal(4, package.Questions.Count);
        Assert.Equal(
            ["PRIMARY_SYMPTOM", "DURATION", "INTENSITY", "ADDITIONAL_SYMPTOMS"],
            package.Questions.OrderBy(value => value.DisplayOrder)
                .Select(value => value.Code.Value));
        Assert.Equal(displayLabel, package.RuleDefinitions.DemoIntake!
            .PrimarySymptomDisplayLabel);
        Assert.Equal(applicableAdditionalCount, package.RuleDefinitions.DemoIntake!
            .ApplicableAdditionalSymptoms.Count);
    }

    [Fact]
    public void AdditionalSymptomCatalog_ContainsExactlyThreeConfirmedValues()
    {
        Assert.Equal(["NAUSEA", "DIARRHEA", "FEVER"], DemoAdditionalSymptoms.Catalog);
        Assert.Equal(3, Enum.GetValues<DemoAdditionalSymptom>().Length);
        Assert.Equal("NAUSEA", DemoAdditionalSymptom.Nausea.ToCode());
        Assert.Equal("DIARRHEA", DemoAdditionalSymptom.Diarrhea.ToCode());
        Assert.Equal("FEVER", DemoAdditionalSymptom.Fever.ToCode());
    }

    [Fact]
    public void Fever_DeterministicallyExcludesFeverFromApplicableAdditionalSymptoms()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Fever);
        var metadata = package.RuleDefinitions.DemoIntake!;
        var question = package.Questions.Single(value =>
            value.Code == metadata.AdditionalSymptomsQuestionCode);

        Assert.Equal(["NAUSEA", "DIARRHEA"], metadata.ApplicableAdditionalSymptoms);
        Assert.Equal(["NAUSEA", "DIARRHEA"], question.Answer.AllowedValues);
        Assert.DoesNotContain("FEVER", metadata.ApplicableAdditionalSymptoms);
    }

    [Theory]
    [InlineData("HEADACHE")]
    [InlineData("ABDOMINAL_PAIN")]
    public void NonFeverPathways_ExposeTheExactGlobalCatalog(string pathwayCode)
    {
        var package = Create(pathwayCode);

        Assert.Equal(
            DemoAdditionalSymptoms.Catalog,
            package.RuleDefinitions.DemoIntake!.ApplicableAdditionalSymptoms);
    }

    [Theory]
    [InlineData("HEADACHE")]
    [InlineData("ABDOMINAL_PAIN")]
    [InlineData("FEVER")]
    public void Package_HasNoClinicalAuthorityArtifacts(string pathwayCode)
    {
        var definitions = Create(pathwayCode).RuleDefinitions;

        Assert.Empty(definitions.Urgencies);
        Assert.Empty(definitions.Dispositions);
        Assert.Empty(definitions.RedFlags);
        Assert.Empty(definitions.Rules);
    }

    [Fact]
    public void CompletenessMetadata_PinsPrimaryAndRequiresOnlyThreeAnsweredFields()
    {
        var metadata = SimplifiedDemoDefinitionPackages
            .Create(ClinicalPathways.Headache).RuleDefinitions.DemoIntake!;

        Assert.Equal("PRIMARY_SYMPTOM", metadata.PrimarySymptomQuestionCode.Value);
        Assert.Equal(
            ["DURATION", "INTENSITY", "ADDITIONAL_SYMPTOMS"],
            metadata.RequiredAnswerQuestionCodes.Select(value => value.Value));
        Assert.Equal(metadata.RequiredAnswerQuestionCodes, metadata.ProgressionQuestionCodes);
        Assert.True(metadata.AdditionalSymptomsAllowsEmptySelection);
    }

    [Fact]
    public void Create_IsDeterministicAndPreservesTheDetailedAbdominalPackage()
    {
        var first = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.AbdominalPain);
        var second = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.AbdominalPain);
        var detailed = AbdominalPainProvisionalPackage.Create();

        Assert.Equal(first.Questionnaire.Id, second.Questionnaire.Id);
        Assert.Equal(first.Questionnaire.ContentHash, second.Questionnaire.ContentHash);
        Assert.Equal(first.RuleSet.Id, second.RuleSet.Id);
        Assert.Equal(first.RuleSet.ContentHash, second.RuleSet.ContentHash);
        Assert.NotEqual(first.Questionnaire.QuestionnaireCode,
            detailed.Questionnaire.QuestionnaireCode);
        Assert.NotEqual(first.Version, detailed.Version);
        Assert.Equal(ClinicalDefinitionPackageProfile.DetailedClinical, detailed.Profile);
        _validator.Validate(detailed);
    }

    [Fact]
    public void Create_RejectsRecognizedButUnsupportedAndUnknownPathways()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.ChestPain));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SimplifiedDemoDefinitionPackages.Create(ClinicalPathwayCode.Create("UNKNOWN")));
    }

    [Fact]
    public void Validator_RejectsAInventedFourthAdditionalSymptom()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var metadata = package.RuleDefinitions.DemoIntake!;
        var invented = metadata with
        {
            AdditionalSymptomCatalog = [.. metadata.AdditionalSymptomCatalog, "COUGH"],
            ApplicableAdditionalSymptoms = [.. metadata.ApplicableAdditionalSymptoms, "COUGH"]
        };
        var questions = package.Questions.Select(value =>
            value.Code == metadata.AdditionalSymptomsQuestionCode
                ? value with
                {
                    Answer = value.Answer with
                    {
                        AllowedValues = [.. value.Answer.AllowedValues!, "COUGH"]
                    }
                }
                : value).ToArray();
        var invalid = Rebuild(package, questions,
            package.RuleDefinitions with { DemoIntake = invented });

        Assert.Throws<ClinicalDefinitionValidationException>(() => _validator.Validate(invalid));
    }

    [Fact]
    public void Validator_RejectsClinicalExecutionArtifactsInDemoProfile()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var definitions = package.RuleDefinitions with
        {
            Urgencies =
            [
                new UrgencyDefinition(ClinicalUrgencies.Low, 1, "Not allowed in demo")
            ]
        };

        Assert.Throws<ClinicalDefinitionValidationException>(() =>
            _validator.Validate(Rebuild(package, package.Questions, definitions)));
    }

    private static ClinicalDefinitionPackage Create(string pathwayCode) =>
        SimplifiedDemoDefinitionPackages.Create(ClinicalPathwayCode.Create(pathwayCode));

    private static ClinicalDefinitionPackage Rebuild(
        ClinicalDefinitionPackage package,
        IReadOnlyList<ClinicalQuestionDefinition> questions,
        ClinicalRulePackageDefinition definitions) => new(
            package.Pathway,
            package.Questionnaire,
            package.RuleSet,
            questions,
            package.Branches,
            definitions);
}
