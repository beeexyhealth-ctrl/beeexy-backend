using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

internal static class ClinicalAnswerValueValidator
{
    public static ClinicalAiValidationIssue? Validate(
        ClinicalAiCandidateValue value,
        ClinicalQuestionDefinition question,
        ClinicalDefinitionPackage definition)
    {
        if (value is null)
        {
            return ClinicalAiValidationIssue.WrongAnswerType;
        }

        var answer = question.Answer;
        return answer.Type switch
        {
            ClinicalAnswerType.FreeText => value is ClinicalAiTextValue text &&
                !string.IsNullOrWhiteSpace(text.Value)
                    ? null
                    : ClinicalAiValidationIssue.WrongAnswerType,
            ClinicalAnswerType.SingleChoice or ClinicalAnswerType.SymptomSelection =>
                ValidateChoice(value, answer),
            ClinicalAnswerType.MultipleChoice =>
                ValidateMultipleChoice(value, answer, question.Code, definition),
            ClinicalAnswerType.IntegerScale => ValidateInteger(value, answer),
            ClinicalAnswerType.Boolean => value is ClinicalAiBooleanValue
                ? null
                : ClinicalAiValidationIssue.WrongAnswerType,
            ClinicalAnswerType.Duration => ValidateDuration(value),
            ClinicalAnswerType.Temperature => ValidateTemperature(value, answer),
            _ => ClinicalAiValidationIssue.WrongAnswerType
        };
    }

    private static ClinicalAiValidationIssue? ValidateChoice(
        ClinicalAiCandidateValue value,
        ClinicalAnswerDefinition answer)
    {
        if (value is not ClinicalAiChoiceValue choice ||
            string.IsNullOrWhiteSpace(choice.Value))
        {
            return ClinicalAiValidationIssue.WrongAnswerType;
        }

        return answer.AllowedValues is { Count: > 0 } allowed &&
            !allowed.Contains(choice.Value, StringComparer.Ordinal)
                ? ClinicalAiValidationIssue.InvalidChoice
                : null;
    }

    private static ClinicalAiValidationIssue? ValidateMultipleChoice(
        ClinicalAiCandidateValue value,
        ClinicalAnswerDefinition answer,
        QuestionCode questionCode,
        ClinicalDefinitionPackage definition)
    {
        if (value is not ClinicalAiMultipleChoiceValue multiple ||
            multiple.Values is null ||
            multiple.Values.Any(string.IsNullOrWhiteSpace) ||
            multiple.Values.Distinct(StringComparer.Ordinal).Count() != multiple.Values.Count)
        {
            return ClinicalAiValidationIssue.WrongAnswerType;
        }

        var allowsEmpty = definition.RuleDefinitions.DemoIntake is { } demo &&
            demo.AdditionalSymptomsQuestionCode == questionCode &&
            demo.AdditionalSymptomsAllowsEmptySelection;
        if (multiple.Values.Count == 0 && !allowsEmpty)
        {
            return ClinicalAiValidationIssue.WrongAnswerType;
        }

        return answer.AllowedValues is { Count: > 0 } allowed &&
            multiple.Values.Any(item => !allowed.Contains(item, StringComparer.Ordinal))
                ? ClinicalAiValidationIssue.InvalidChoice
                : null;
    }

    private static ClinicalAiValidationIssue? ValidateInteger(
        ClinicalAiCandidateValue value,
        ClinicalAnswerDefinition answer)
    {
        if (value is not ClinicalAiIntegerValue integer)
        {
            return ClinicalAiValidationIssue.WrongAnswerType;
        }

        return (answer.Minimum.HasValue && integer.Value < answer.Minimum.Value) ||
            (answer.Maximum.HasValue && integer.Value > answer.Maximum.Value)
            ? ClinicalAiValidationIssue.ValueOutsideRange
            : null;
    }

    private static ClinicalAiValidationIssue? ValidateDuration(ClinicalAiCandidateValue value) =>
        value is ClinicalAiDurationValue duration &&
        duration.Value > 0 &&
        Enum.IsDefined(duration.Unit)
            ? null
            : ClinicalAiValidationIssue.InvalidDuration;

    private static ClinicalAiValidationIssue? ValidateTemperature(
        ClinicalAiCandidateValue value,
        ClinicalAnswerDefinition answer)
    {
        if (value is not ClinicalAiTemperatureValue temperature ||
            !Enum.IsDefined(temperature.Unit))
        {
            return ClinicalAiValidationIssue.InvalidTemperature;
        }

        return answer.Unit is null || string.Equals(
            answer.Unit,
            temperature.Unit.ToString(),
            StringComparison.OrdinalIgnoreCase)
                ? null
                : ClinicalAiValidationIssue.InvalidTemperature;
    }
}
