using System.Text.Json;
using Beeexy.Application.Care;
using Beeexy.Domain.Care;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Care;

namespace Beeexy.Tests.Unit.Care;

[Trait("Category", "Phase95")]
public sealed class SymptomDiaryAnswerStructureValidatorTests
{
    private readonly SymptomDiaryAnswerStructureValidator validator = new();

    [Theory]
    [InlineData("HEADACHE")]
    [InlineData("ABDOMINAL_PAIN")]
    [InlineData("FEVER")]
    [InlineData("CHEST_PAIN")]
    public void EveryAndreaPathwayAcceptsItsExactStructuralAnswers(string pathwayValue)
    {
        var package = AndreaSymptomDiaryPackages.Create(
            ClinicalPathwayCode.Create(pathwayValue));
        var submitted = package.Questions.Select(question =>
            new SymptomDiarySubmittedAnswer(
                question.Code.Value,
                ValidValue(question))).ToArray();

        var result = validator.Validate(package, submitted);

        Assert.Equal(package.Questions.Count, result.Count);
        Assert.Equal(
            package.Questions.Select(question => question.Code),
            result.Select(answer => answer.Question.Code));
    }

    [Fact]
    public void ExactSingleChoiceIsAcceptedAndUnknownValueIsRejected()
    {
        var package = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        var question = package.Questions.First(value => value.Options.Count > 0 &&
            JsonDocument.Parse(value.AnswerSchemaJson).RootElement
                .GetProperty("type").GetString() == "string");
        var exact = question.Options[0].Value;

        var result = validator.Validate(package,
            [new(question.Code.Value, Json(exact))]);

        Assert.Equal(exact, result[0].Value.GetString());
        Assert.Throws<SymptomDiaryAnswerValidationException>(() => validator.Validate(
            package,
            [new(question.Code.Value, Json(exact + " "))]));
    }

    [Fact]
    public void MultipleChoicePreservesOrderAndRejectsUnknownOrDuplicateValues()
    {
        var package = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        var question = package.Questions.First(value =>
            JsonDocument.Parse(value.AnswerSchemaJson).RootElement
                .GetProperty("type").GetString() == "array");
        var values = question.Options.Take(2).Select(value => value.Value).ToArray();

        var result = validator.Validate(package,
            [new(question.Code.Value, Json(values))]);

        Assert.Equal(values, result[0].Value.EnumerateArray().Select(value => value.GetString()));
        Assert.Throws<SymptomDiaryAnswerValidationException>(() => validator.Validate(
            package,
            [new(question.Code.Value, Json(new[] { values[0], values[0] }))]));
        Assert.Throws<SymptomDiaryAnswerValidationException>(() => validator.Validate(
            package,
            [new(question.Code.Value, Json(new[] { "not-an-option" }))]));
    }

    [Fact]
    public void FreeTextPreservesUnicodeAndPunctuationWithoutMedicalInterpretation()
    {
        var package = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Fever);
        var question = package.Questions.First(value => value.Options.Count == 0);
        const string exact = "Desde ayer: 38.0–38.9°C; sensación ≥ moderada.";

        var result = validator.Validate(package,
            [new(question.Code.Value, Json(exact))]);

        Assert.Equal(exact, result[0].Value.GetString());
        Assert.Equal(JsonSerializer.Serialize(exact), result[0].SubmittedValueJson);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{}")]
    public void MalformedShapeIsRejected(string rawValue)
    {
        var package = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Fever);
        var question = package.Questions.First(value => value.Options.Count == 0);

        Assert.Throws<SymptomDiaryAnswerValidationException>(() => validator.Validate(
            package,
            [new(question.Code.Value, JsonRaw(rawValue))]));
    }

    [Fact]
    public void UnknownCrossPackageDuplicateAndUnsupportedAnswerFieldsAreRejected()
    {
        var package = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        var question = package.Questions[0];
        var valid = ValidValue(question);
        var otherQuestion = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Fever)
            .Questions.Select(value => value.Code.Value)
            .First(code => package.Questions.All(question => question.Code.Value != code));

        Assert.Throws<SymptomDiaryAnswerValidationException>(() => validator.Validate(
            package,
            [new("unknown-question", Json("value"))]));
        Assert.Throws<SymptomDiaryAnswerValidationException>(() => validator.Validate(
            package,
            [new(otherQuestion, Json("value"))]));
        Assert.Throws<SymptomDiaryAnswerValidationException>(() => validator.Validate(
            package,
            [new(question.Code.Value, valid), new(question.Code.Value, valid)]));
        Assert.Throws<SymptomDiaryAnswerValidationException>(() => validator.Validate(
            package,
            [new(question.Code.Value, valid, HasUnsupportedFields: true)]));
    }

    [Fact]
    public void OptionalOmissionCreatesNoDefaultButPackageRequirednessIsEnforced()
    {
        var package = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        Assert.Empty(validator.Validate(package, []));

        var required = package with
        {
            Questions =
            [
                package.Questions[0] with { IsRequired = true },
                .. package.Questions.Skip(1)
            ]
        };
        Assert.Throws<SymptomDiaryAnswerValidationException>(() =>
            validator.Validate(required, []));
    }

    [Fact]
    public void InconsistentPackageSchemaFailsAsContentIntegrityNotPatientValidation()
    {
        var package = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        var question = package.Questions[0] with
        {
            AnswerSchemaJson = "{\"type\":\"number\"}"
        };

        Assert.Throws<SymptomDiaryPackageIntegrityException>(() => validator.Validate(
            package with { Questions = [question, .. package.Questions.Skip(1)] },
            [new(question.Code.Value, JsonRaw("1"))]));
    }

    [Fact]
    public void LogicalRetryHashIgnoresAnswerArrayOrderButDetectsMaterialChange()
    {
        var package = AndreaSymptomDiaryPackages.Create(ClinicalPathways.Headache);
        var selected = package.Questions.Take(2).ToArray();
        var first = validator.Validate(package, selected.Select(question =>
            new SymptomDiarySubmittedAnswer(question.Code.Value, ValidValue(question)))
            .ToArray());
        var reordered = validator.Validate(package, selected.Reverse().Select(question =>
            new SymptomDiarySubmittedAnswer(question.Code.Value, ValidValue(question)))
            .ToArray());
        var packageId = EntityId.New();

        var firstHash = SymptomCheckInRequestHashCalculator.Calculate(packageId, first);
        var reorderedHash = SymptomCheckInRequestHashCalculator.Calculate(packageId, reordered);
        var differentPackageHash = SymptomCheckInRequestHashCalculator.Calculate(
            EntityId.New(),
            reordered);

        Assert.Equal(firstHash, reorderedHash);
        Assert.NotEqual(firstHash, differentPackageHash);
    }

    private static JsonElement ValidValue(SymptomDiaryQuestionDefinition question)
    {
        using var schema = JsonDocument.Parse(question.AnswerSchemaJson);
        return schema.RootElement.GetProperty("type").GetString() switch
        {
            "array" => Json(question.Options.Take(2).Select(value => value.Value).ToArray()),
            "string" when question.Options.Count > 0 => Json(question.Options[0].Value),
            "string" => Json("Texto exacto del paciente"),
            _ => throw new InvalidOperationException()
        };
    }

    private static JsonElement Json<T>(T value) =>
        JsonSerializer.SerializeToElement(value);

    private static JsonElement JsonRaw(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
