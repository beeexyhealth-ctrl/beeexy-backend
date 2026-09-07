using System.Globalization;
using System.Text.Json;
using Beeexy.Application.Care;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;

namespace Beeexy.Tests.Unit.Care;

[Trait("Category", "Phase92")]
public sealed class SymptomDiaryPackageInfrastructureTests
{
    private readonly SymptomDiaryPackageValidator _validator = new();
    private readonly SymptomDiaryPackageCanonicalSerializer _serializer = new();

    [Fact]
    public void ValidSyntheticPackage_PassesValidationAndIsDisplayEligible()
    {
        var package = CreatePackage();

        _validator.Validate(package);

        Assert.True(_validator.IsDisplayEligible(package));
    }

    [Fact]
    public void MissingIdentityOrVersion_FailsClosed()
    {
        var package = CreatePackage();
        var invalid = new[]
        {
            package with { PackageCode = null! },
            package with { PackageVersion = null! },
            package with { QuestionSetCode = null! },
            package with { QuestionSetVersion = null! },
            package with { SymptomInformationCode = null! },
            package with { SymptomInformationVersion = null! }
        };

        foreach (var value in invalid)
        {
            Assert.Throws<SymptomDiaryPackageValidationException>(() =>
                _validator.Validate(value));
        }
    }

    [Fact]
    public void DuplicateQuestionCodeOrOrder_FailsClosed()
    {
        var package = CreatePackage();
        var first = package.Questions[0];
        var second = package.Questions[1];

        Assert.Throws<SymptomDiaryPackageValidationException>(() => _validator.Validate(
            package with { Questions = [first, second with { Code = first.Code }] }));
        Assert.Throws<SymptomDiaryPackageValidationException>(() => _validator.Validate(
            package with { Questions = [first, second with { SourceOrder = first.SourceOrder }] }));
    }

    [Fact]
    public void DuplicateOptionCodeOrOrder_FailsClosed()
    {
        var package = CreatePackage();
        var question = package.Questions[0];
        var first = question.Options[0];
        var second = question.Options[1];

        Assert.Throws<SymptomDiaryPackageValidationException>(() => _validator.Validate(
            package with
            {
                Questions =
                [
                    question with { Options = [first, second with { Code = first.Code }] },
                    package.Questions[1]
                ]
            }));
        Assert.Throws<SymptomDiaryPackageValidationException>(() => _validator.Validate(
            package with
            {
                Questions =
                [
                    question with
                    {
                        Options = [first, second with { SourceOrder = first.SourceOrder }]
                    },
                    package.Questions[1]
                ]
            }));
    }

    [Fact]
    public void DuplicateWarningCodeOrOrder_FailsClosed()
    {
        var package = CreatePackage();
        var first = package.WarningSigns[0];
        var second = package.WarningSigns[1];

        Assert.Throws<SymptomDiaryPackageValidationException>(() => _validator.Validate(
            package with { WarningSigns = [first, second with { Code = first.Code }] }));
        Assert.Throws<SymptomDiaryPackageValidationException>(() => _validator.Validate(
            package with
            {
                WarningSigns = [first, second with { SourceOrder = first.SourceOrder }]
            }));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"type\":\"string\",\"type\":\"number\"}")]
    public void InvalidAnswerSchema_FailsClosed(string schema)
    {
        var package = CreatePackage();
        var question = package.Questions[0] with { AnswerSchemaJson = schema };

        Assert.Throws<SymptomDiaryPackageValidationException>(() => _validator.Validate(
            package with { Questions = [question, package.Questions[1]] }));
    }

    [Fact]
    public void UnrecognizedPathway_FailsClosed()
    {
        var package = CreatePackage() with
        {
            Pathway = ClinicalPathwayCode.Create("SYNTHETIC_UNRECOGNIZED_PATHWAY")
        };

        Assert.Throws<SymptomDiaryPackageValidationException>(() =>
            _validator.Validate(package));
    }

    [Fact]
    public void MissingSourceReference_FailsClosed()
    {
        Assert.Throws<SymptomDiaryPackageValidationException>(() =>
            _validator.Validate(CreatePackage() with { SourceReference = " " }));
    }

    [Fact]
    public void ImpossibleProvenanceAndActivation_FailClosed()
    {
        var package = CreatePackage();
        var inconsistent = package with
        {
            ContentStatus = new ClinicalContentStatus(
                ClinicalContentSource.MedicalTeamProvided,
                ClinicalReviewStatus.Provisional,
                ClinicalApprovalStatus.Approved)
        };
        var activatedProvisional = package with
        {
            ContentStatus = ClinicalContentStatus.ProvisionalReferencePlatformDerived,
            ApprovedAt = null,
            ActivatedAt = ActivatedAt
        };
        var approvedDemo = package with
        {
            ContentStatus = new ClinicalContentStatus(
                ClinicalContentSource.ProductDemoDefined,
                ClinicalReviewStatus.Reviewed,
                ClinicalApprovalStatus.Approved)
        };

        Assert.Throws<SymptomDiaryPackageValidationException>(() =>
            _validator.Validate(inconsistent));
        Assert.Throws<SymptomDiaryPackageValidationException>(() =>
            _validator.Validate(activatedProvisional));
        Assert.Throws<SymptomDiaryPackageValidationException>(() =>
            _validator.Validate(approvedDemo));
    }

    [Fact]
    public void NonUtcOrChronologicallyImpossibleTimestamps_FailClosed()
    {
        var package = CreatePackage();
        var localOffset = package with
        {
            ImportedAt = package.ImportedAt.ToOffset(TimeSpan.FromHours(-5))
        };
        var approvalBeforeImport = package with
        {
            ApprovedAt = package.ImportedAt.AddSeconds(-1)
        };
        var activationBeforeApproval = package with
        {
            ActivatedAt = package.ApprovedAt!.Value.AddSeconds(-1)
        };

        Assert.Throws<SymptomDiaryPackageValidationException>(() =>
            _validator.Validate(localOffset));
        Assert.Throws<SymptomDiaryPackageValidationException>(() =>
            _validator.Validate(approvalBeforeImport));
        Assert.Throws<SymptomDiaryPackageValidationException>(() =>
            _validator.Validate(activationBeforeApproval));
    }

    [Fact]
    public void CanonicalSerialization_IsIndependentOfCollectionAndJsonPropertyOrder()
    {
        var package = CreatePackage();
        var reorderedQuestions = package.Questions.Reverse()
            .Select(question => question.SourceOrder == 1
                ? question with
                {
                    AnswerSchemaJson =
                        "{\"zeta\":{\"beta\":2,\"alpha\":1},\"type\":\"string\"}",
                    Options = question.Options.Reverse().ToArray()
                }
                : question)
            .ToArray();
        var reordered = package with
        {
            Questions = reorderedQuestions,
            WarningSigns = package.WarningSigns.Reverse().ToArray()
        };

        Assert.Equal(_serializer.Serialize(package), _serializer.Serialize(reordered));
        Assert.Equal(Hash(package), Hash(reordered));
    }

    [Fact]
    public void CanonicalSerialization_PreservesExactUnicodeAndIsCultureInvariant()
    {
        var package = CreatePackage() with
        {
            InformationalHeading = "Información sintética — niño 👩🏽‍⚕️",
            InformationalBody = "Línea uno\nLínea dos: café, piñata, 中文"
        };
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var first = _serializer.Serialize(package);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-PE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-PE");
            var second = _serializer.Serialize(package);

            Assert.Equal(first, second);
            using var json = JsonDocument.Parse(first);
            Assert.Equal(
                "Información sintética — niño 👩🏽‍⚕️",
                json.RootElement.GetProperty("informationalHeading").GetString());
            Assert.Equal(
                "Línea uno\nLínea dos: café, piñata, 中文",
                json.RootElement.GetProperty("informationalBody").GetString());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Sha256_IsDeterministicAndLowercase()
    {
        var first = Hash(CreatePackage()).Value;
        var second = Hash(CreatePackage()).Value;

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void EveryPersistedContentOrOrderChange_ChangesHash()
    {
        var package = CreatePackage();
        var baseline = Hash(package);
        var question = package.Questions[0];
        var option = question.Options[0];
        var warning = package.WarningSigns[0];
        var mutations = new[]
        {
            package with { InformationalBody = package.InformationalBody + " changed" },
            package with
            {
                Questions = [question with { PromptText = "Changed synthetic prompt" }, package.Questions[1]]
            },
            package with
            {
                Questions =
                [
                    question with
                    {
                        Options =
                        [
                            option with { DisplayText = "Changed synthetic display" },
                            question.Options[1]
                        ]
                    },
                    package.Questions[1]
                ]
            },
            package with
            {
                Questions =
                [
                    question with
                    {
                        Options =
                        [option with { Value = "changed-value" }, question.Options[1]]
                    },
                    package.Questions[1]
                ]
            },
            package with
            {
                Questions =
                [
                    question with
                    {
                        Options =
                        [
                            option with { SourceOrder = 2 },
                            question.Options[1] with { SourceOrder = 1 }
                        ]
                    },
                    package.Questions[1]
                ]
            },
            package with
            {
                WarningSigns =
                [warning with { DisplayText = "Changed synthetic information" }, package.WarningSigns[1]]
            },
            package with
            {
                WarningSigns =
                [
                    warning with { SourceOrder = 2 },
                    package.WarningSigns[1] with { SourceOrder = 1 }
                ]
            },
            package with
            {
                Questions =
                [
                    question with { SourceOrder = 2 },
                    package.Questions[1] with { SourceOrder = 1 }
                ]
            }
        };

        Assert.All(mutations, mutation => Assert.NotEqual(baseline, Hash(mutation)));
    }

    [Fact]
    public void ImportTimestamp_IsOperationalMetadataAndDoesNotChangeContentIdentity()
    {
        var package = CreatePackage();

        Assert.Equal(Hash(package), Hash(package with { ImportedAt = ImportedAt.AddDays(9) }));
    }

    [Fact]
    public async Task ProviderContract_SeparatesActiveLookupFromExactHistoricalLookup()
    {
        var historical = Content(EntityId.New(), CreatePackage("v1"));
        var active = Content(EntityId.New(), CreatePackage("v2"));
        ISymptomDiaryContentProvider provider = new ContractProbeProvider(active, historical);

        Assert.Same(active, await provider.GetActivePackageAsync(ClinicalPathways.BackPain));
        Assert.Same(historical, await provider.GetExactPackageAsync(historical.PackageVersionId));
    }

    private SymptomDiarySha256 Hash(SymptomDiaryPackageDefinition package) =>
        new SymptomDiaryPackageHashCalculator(_serializer).Calculate(package);

    private SymptomDiaryPackageContent Content(
        EntityId id,
        SymptomDiaryPackageDefinition package) => new(id, Hash(package), package);

    private static SymptomDiaryPackageDefinition CreatePackage(string version = "v1") => new(
        SymptomDiaryCode.Create("synthetic-diary-package"),
        DefinitionVersion.Create(version),
        SymptomDiaryCode.Create("synthetic-question-set"),
        DefinitionVersion.Create("questions-v1"),
        SymptomDiaryCode.Create("synthetic-information"),
        DefinitionVersion.Create("information-v1"),
        ClinicalPathways.BackPain,
        ClinicalContentStatus.MedicalTeamApproved,
        "test/phase-9-2/synthetic-package",
        ImportedAt,
        ApprovedAt,
        ActivatedAt,
        "Synthetic informational heading",
        "Synthetic informational body",
        [
            new(
                SymptomDiaryCode.Create("synthetic-question-a"),
                "Synthetic prompt A",
                1,
                "{\"type\":\"string\",\"zeta\":{\"alpha\":1,\"beta\":2}}",
                true,
                [
                    new(SymptomDiaryCode.Create("synthetic-option-a"), "a", "Synthetic A", 1),
                    new(SymptomDiaryCode.Create("synthetic-option-b"), "b", "Synthetic B", 2)
                ]),
            new(
                SymptomDiaryCode.Create("synthetic-question-b"),
                "Synthetic prompt B",
                2,
                "{\"type\":\"boolean\"}",
                false,
                [])
        ],
        [
            new(SymptomDiaryCode.Create("synthetic-information-a"), "Synthetic information A", 1),
            new(SymptomDiaryCode.Create("synthetic-information-b"), "Synthetic information B", 2)
        ]);

    private static readonly DateTimeOffset ImportedAt =
        new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ApprovedAt = ImportedAt.AddHours(1);
    private static readonly DateTimeOffset ActivatedAt = ImportedAt.AddHours(2);

    private sealed class ContractProbeProvider(
        SymptomDiaryPackageContent active,
        SymptomDiaryPackageContent historical) : ISymptomDiaryContentProvider
    {
        public Task<SymptomDiaryPackageContent?> GetActivePackageAsync(
            ClinicalPathwayCode pathway,
            CancellationToken cancellationToken = default) => Task.FromResult(
                pathway == active.Definition.Pathway ? active : null);

        public Task<SymptomDiaryPackageContent?> GetExactPackageAsync(
            EntityId packageVersionId,
            CancellationToken cancellationToken = default) => Task.FromResult(
                packageVersionId == historical.PackageVersionId ? historical : null);
    }
}
