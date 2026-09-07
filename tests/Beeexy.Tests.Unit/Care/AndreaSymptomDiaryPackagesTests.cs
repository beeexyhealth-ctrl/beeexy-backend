using System.Security.Cryptography;
using System.Text.Json;
using Beeexy.Application.Care;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;

namespace Beeexy.Tests.Unit.Care;

[Trait("Category", "Phase93")]
public sealed class AndreaSymptomDiaryPackagesTests
{
    private readonly SymptomDiaryPackageCanonicalSerializer _serializer = new();

    [Fact]
    public void SourceArtifact_HasExpectedImmutableByteChecksum()
    {
        var sourceBytes = File.ReadAllBytes(SourcePath());
        var hash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();

        Assert.Equal(AndreaSymptomDiaryPackages.SourceSha256, hash);
    }

    [Fact]
    public void Releases_HaveStableAuditableIdentityAndApprovedActiveProvenance()
    {
        var packages = AndreaSymptomDiaryPackages.CreateAll();

        Assert.Equal(
            [
                ClinicalPathways.ChestPain,
                ClinicalPathways.AbdominalPain,
                ClinicalPathways.Fever,
                ClinicalPathways.Headache
            ],
            packages.Select(value => value.Pathway));
        Assert.All(packages, package =>
        {
            var slug = package.Pathway.Value.ToLowerInvariant().Replace('_', '-');
            Assert.Equal($"andrea-{slug}-symptom-diary", package.PackageCode.Value);
            Assert.Equal($"andrea-{slug}-questions", package.QuestionSetCode.Value);
            Assert.Equal(
                $"andrea-{slug}-warning-information",
                package.SymptomInformationCode.Value);
            Assert.Equal(
                AndreaSymptomDiaryPackages.VersionIdentifier,
                package.PackageVersion.Value);
            Assert.Equal(package.PackageVersion, package.QuestionSetVersion);
            Assert.Equal(package.PackageVersion, package.SymptomInformationVersion);
            Assert.Equal(
                ClinicalContentSource.MedicalTeamProvided,
                package.ContentStatus.Source);
            Assert.Equal(ClinicalReviewStatus.Reviewed, package.ContentStatus.ReviewStatus);
            Assert.Equal(
                ClinicalApprovalStatus.Approved,
                package.ContentStatus.ApprovalStatus);
            Assert.Equal(AndreaSymptomDiaryPackages.SourceReference, package.SourceReference);
            Assert.Equal(AndreaSymptomDiaryPackages.ApprovalEffectiveAt, package.ApprovedAt);
            Assert.Equal(AndreaSymptomDiaryPackages.ApprovalEffectiveAt, package.ActivatedAt);
            Assert.Equal("Red flags:", package.InformationalHeading);
            Assert.Null(package.InformationalBody);
        });
    }

    [Theory]
    [InlineData("Chest pain", "CHEST_PAIN")]
    [InlineData("Abdominal pain", "ABDOMINAL_PAIN")]
    [InlineData("Fever", "FEVER")]
    [InlineData("Headache", "HEADACHE")]
    public void Release_IsFieldForFieldFaithfulToExplicitMarkdownStructure(
        string sourceHeading,
        string pathwayCode)
    {
        var source = ParseSource().Single(section => section.Heading == sourceHeading);
        var package = AndreaSymptomDiaryPackages.Create(
            ClinicalPathwayCode.Create(pathwayCode));

        Assert.Equal(source.Questions.Count, package.Questions.Count);
        Assert.Equal(source.Warnings, package.WarningSigns.Select(value => value.DisplayText));
        Assert.Equal(
            Enumerable.Range(1, source.Warnings.Count),
            package.WarningSigns.Select(value => value.SourceOrder));
        for (var index = 0; index < source.Questions.Count; index++)
        {
            var expected = source.Questions[index];
            var actual = package.Questions[index];
            Assert.Equal(index + 1, actual.SourceOrder);
            Assert.Equal(expected.Text, actual.PromptText);
            Assert.False(actual.IsRequired);
            Assert.Equal(expected.Options, actual.Options.Select(value => value.DisplayText));
            Assert.Equal(expected.Options, actual.Options.Select(value => value.Value));
            Assert.Equal(
                Enumerable.Range(1, expected.Options.Count),
                actual.Options.Select(value => value.SourceOrder));
            AssertAnswerSchema(expected, actual.AnswerSchemaJson);
        }
    }

    [Fact]
    public void TechnicalCodes_AreStableAndContainNoClinicalOutcomeMetadata()
    {
        var packages = AndreaSymptomDiaryPackages.CreateAll();
        var repeated = AndreaSymptomDiaryPackages.CreateAll();

        Assert.Equal(
            ["onset", "severity", "character", "associated-symptoms"],
            packages[0].Questions.Select(value => value.Code.Value));
        Assert.Equal(
            ["onset", "location", "severity", "associated-symptoms"],
            packages[1].Questions.Select(value => value.Code.Value));
        Assert.Equal(
            ["onset", "highest-temperature", "associated-symptoms", "overall-feeling"],
            packages[2].Questions.Select(value => value.Code.Value));
        Assert.Equal(
            ["onset", "maximum-intensity-onset", "severity", "associated-symptoms"],
            packages[3].Questions.Select(value => value.Code.Value));
        Assert.Equal(
            packages.SelectMany(PackageCodes),
            repeated.SelectMany(PackageCodes));
        Assert.All(packages, package => Assert.Equal(
            Enumerable.Range(1, package.WarningSigns.Count)
                .Select(value => $"warning-{value:00}"),
            package.WarningSigns.Select(value => value.Code.Value)));

        var contractNames = typeof(SymptomDiaryPackageDefinition).GetProperties()
            .Select(value => value.Name)
            .Concat(typeof(SymptomDiaryQuestionDefinition).GetProperties()
                .Select(value => value.Name))
            .Concat(typeof(SymptomDiaryQuestionOptionDefinition).GetProperties()
                .Select(value => value.Name))
            .Concat(typeof(SymptomWarningSignDefinition).GetProperties()
                .Select(value => value.Name))
            .ToArray();
        Assert.DoesNotContain(contractNames, IsExecutableClinicalConcept);
    }

    [Theory]
    [InlineData("CHEST_PAIN", AndreaSymptomDiaryPackages.ChestPainContentSha256)]
    [InlineData("ABDOMINAL_PAIN", AndreaSymptomDiaryPackages.AbdominalPainContentSha256)]
    [InlineData("FEVER", AndreaSymptomDiaryPackages.FeverContentSha256)]
    [InlineData("HEADACHE", AndreaSymptomDiaryPackages.HeadacheContentSha256)]
    public void ReleaseHash_IsTheRecordedDeterministicLowercaseSha256(
        string pathwayCode,
        string expectedHash)
    {
        var package = AndreaSymptomDiaryPackages.Create(
            ClinicalPathwayCode.Create(pathwayCode));
        var calculator = new SymptomDiaryPackageHashCalculator(_serializer);

        var first = calculator.Calculate(package).Value;
        var second = calculator.Calculate(
            AndreaSymptomDiaryPackages.Create(package.Pathway)).Value;

        Assert.Equal(expectedHash, first);
        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void EveryRelease_ValidatesAndIsActiveWithApprovalProvenance()
    {
        var validator = new SymptomDiaryPackageValidator();

        foreach (var package in AndreaSymptomDiaryPackages.CreateAll())
        {
            validator.Validate(package);
            Assert.True(validator.IsDisplayEligible(package));
        }
    }

    private static IEnumerable<string> PackageCodes(SymptomDiaryPackageDefinition package) =>
        package.Questions.Select(value => value.Code.Value)
            .Concat(package.Questions.SelectMany(question =>
                question.Options.Select(option => option.Code.Value)))
            .Concat(package.WarningSigns.Select(value => value.Code.Value));

    private static bool IsExecutableClinicalConcept(string value) =>
        value.Contains("Condition", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Threshold", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Score", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Risk", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Urgency", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Recommendation", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Action", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Reminder", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("WarningMatch", StringComparison.OrdinalIgnoreCase);

    private static void AssertAnswerSchema(SourceQuestion source, string schemaJson)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        var root = schema.RootElement;
        if (source.Multiple)
        {
            Assert.Equal("array", root.GetProperty("type").GetString());
            Assert.True(root.GetProperty("uniqueItems").GetBoolean());
            Assert.Equal(
                source.Options,
                root.GetProperty("items").GetProperty("enum").EnumerateArray()
                    .Select(value => value.GetString()));
            return;
        }

        Assert.Equal("string", root.GetProperty("type").GetString());
        if (source.Options.Count == 0)
        {
            Assert.False(root.TryGetProperty("enum", out _));
        }
        else
        {
            Assert.Equal(
                source.Options,
                root.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        }
    }

    private static IReadOnlyList<SourceSection> ParseSource()
    {
        var sections = new List<SourceSection>();
        SourceSection? section = null;
        SourceQuestion? question = null;
        foreach (var line in File.ReadAllLines(SourcePath()))
        {
            if (line.StartsWith("### **", StringComparison.Ordinal))
            {
                section = new SourceSection(line[6..^2], [], []);
                sections.Add(section);
                question = null;
            }
            else if (line.StartsWith("* **", StringComparison.Ordinal))
            {
                var closing = line.IndexOf("**", 4, StringComparison.Ordinal);
                question = new SourceQuestion(
                    line[4..closing],
                    line.Contains("*(multiple)*", StringComparison.Ordinal),
                    []);
                Assert.NotNull(section);
                section.Questions.Add(question);
            }
            else if (line.StartsWith("  * ☐ ", StringComparison.Ordinal))
            {
                Assert.NotNull(question);
                question.Options.Add(UnescapeMarkdown(line[6..].TrimEnd()));
            }
            else if (line.StartsWith("**Red flags:** ", StringComparison.Ordinal))
            {
                Assert.NotNull(section);
                section.Warnings.AddRange(
                    UnescapeMarkdown(line[15..]).Split("; ", StringSplitOptions.None));
            }
        }

        return sections;
    }

    private static string UnescapeMarkdown(string value) => value
        .Replace("\\<", "<", StringComparison.Ordinal)
        .Replace("\\>", ">", StringComparison.Ordinal)
        .Replace("\\+", "+", StringComparison.Ordinal);

    private static string SourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "symptoms.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The authoritative docs/symptoms.md was not found.");
    }

    private sealed record SourceSection(
        string Heading,
        List<SourceQuestion> Questions,
        List<string> Warnings);

    private sealed record SourceQuestion(
        string Text,
        bool Multiple,
        List<string> Options);
}
