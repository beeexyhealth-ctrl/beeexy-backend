using System.Globalization;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class ClinicalDefinitionValidationException(string message)
    : InvalidOperationException(message);

public sealed class ClinicalDefinitionPackageValidator
{
    public void Validate(ClinicalDefinitionPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (!ClinicalPathways.Supported.Contains(package.Pathway))
        {
            throw Invalid("Only a registered supported pathway can have a clinical package.");
        }

        if (package.Questionnaire.ActivatedAt != package.RuleSet.ActivatedAt)
        {
            throw Invalid("Questionnaire and rule-set activation metadata must match.");
        }

        ValidateProvenance(package);
        var questions = ValidateQuestions(package);
        ValidateBranches(package.Branches, questions);
        if (package.Profile == ClinicalDefinitionPackageProfile.SimplifiedDemoIntake)
        {
            ValidateDemoPackage(package, questions);
        }
        else
        {
            ValidateRulePackage(package.RuleDefinitions, questions);
        }
    }

    private static void ValidateProvenance(ClinicalDefinitionPackage package)
    {
        if (package.Profile == ClinicalDefinitionPackageProfile.SimplifiedDemoIntake)
        {
            if (package.ContentStatus != ClinicalContentStatus.NonClinicalDemo ||
                package.Questionnaire.ApprovedAt.HasValue ||
                package.RuleSet.ApprovedAt.HasValue)
            {
                throw Invalid(
                    "A simplified demo package must be product/demo-defined, " +
                    "non-clinical, and not clinically approved.");
            }

            return;
        }

        var approved = package.ContentStatus.ApprovalStatus ==
            ClinicalApprovalStatus.Approved;
        var reviewed = package.ContentStatus.ReviewStatus == ClinicalReviewStatus.Reviewed;
        if (package.ContentStatus.Source == ClinicalContentSource.LegacyUnspecified ||
            reviewed != approved ||
            package.Questionnaire.ApprovedAt.HasValue != approved ||
            package.RuleSet.ApprovedAt.HasValue != approved)
        {
            throw Invalid("Clinical approval status and approval timestamps must agree.");
        }
    }

    private static void ValidateDemoPackage(
        ClinicalDefinitionPackage package,
        IReadOnlyDictionary<QuestionCode, ClinicalQuestionDefinition> questions)
    {
        var definition = package.RuleDefinitions.DemoIntake ?? throw Invalid(
            "A simplified demo package requires deterministic intake metadata.");
        if (package.Branches.Count != 0)
        {
            throw Invalid("A simplified demo package cannot contain clinical branches.");
        }

        if (package.RuleDefinitions.Urgencies.Count != 0 ||
            package.RuleDefinitions.Dispositions.Count != 0 ||
            package.RuleDefinitions.RedFlags.Count != 0 ||
            package.RuleDefinitions.Rules.Count != 0)
        {
            throw Invalid(
                "A simplified demo package cannot contain urgency, disposition, red-flag, " +
                "or clinical-rule artifacts.");
        }

        if (string.IsNullOrWhiteSpace(definition.PrimarySymptomDisplayLabel) ||
            !definition.AdditionalSymptomsAllowsEmptySelection)
        {
            throw Invalid(
                "Demo intake metadata must define a display label and allow no additional symptoms.");
        }

        var expectedCodes = new[]
        {
            definition.PrimarySymptomQuestionCode,
            definition.DurationQuestionCode,
            definition.IntensityQuestionCode,
            definition.AdditionalSymptomsQuestionCode
        };
        if (questions.Count != expectedCodes.Length ||
            expectedCodes.Distinct().Count() != expectedCodes.Length ||
            !questions.Keys.ToHashSet().SetEquals(expectedCodes))
        {
            throw Invalid("A simplified demo package must contain exactly its four intake fields.");
        }

        var primary = questions[definition.PrimarySymptomQuestionCode];
        var duration = questions[definition.DurationQuestionCode];
        var intensity = questions[definition.IntensityQuestionCode];
        var additional = questions[definition.AdditionalSymptomsQuestionCode];
        if (primary.Answer.Type != ClinicalAnswerType.SymptomSelection ||
            primary.Answer.AllowedValues is not { Count: 1 } primaryValues ||
            primaryValues[0] != package.Pathway.Value)
        {
            throw Invalid("The demo primary symptom must be pinned to the package pathway.");
        }

        if (duration.Answer.Type != ClinicalAnswerType.Duration)
        {
            throw Invalid("The demo duration field must use the duration answer type.");
        }

        if (intensity.Answer.Type != ClinicalAnswerType.IntegerScale ||
            intensity.Answer.Minimum != 1 || intensity.Answer.Maximum != 10)
        {
            throw Invalid("The demo intensity field must be an integer scale from 1 through 10.");
        }

        if (additional.Answer.Type != ClinicalAnswerType.MultipleChoice ||
            additional.Answer.AllowedValues is null ||
            !additional.Answer.AllowedValues.SequenceEqual(
                definition.ApplicableAdditionalSymptoms, StringComparer.Ordinal))
        {
            throw Invalid(
                "The additional-symptom question must use the package's applicable choices.");
        }

        if (!definition.AdditionalSymptomCatalog.SequenceEqual(
                DemoAdditionalSymptoms.Catalog, StringComparer.Ordinal))
        {
            throw Invalid("The demo additional-symptom catalog must contain exactly three values.");
        }

        var expectedApplicable = package.Pathway == ClinicalPathways.Fever
            ? DemoAdditionalSymptoms.Catalog
                .Where(value => value != DemoAdditionalSymptoms.FeverCode).ToArray()
            : DemoAdditionalSymptoms.Catalog;
        if (!definition.ApplicableAdditionalSymptoms.SequenceEqual(
                expectedApplicable, StringComparer.Ordinal))
        {
            throw Invalid(
                "Applicable additional symptoms must deterministically exclude the primary symptom.");
        }

        var expectedProgression = new[]
        {
            definition.DurationQuestionCode,
            definition.IntensityQuestionCode,
            definition.AdditionalSymptomsQuestionCode
        };
        if (!definition.RequiredAnswerQuestionCodes.SequenceEqual(expectedProgression) ||
            !definition.ProgressionQuestionCodes.SequenceEqual(expectedProgression))
        {
            throw Invalid(
                "Demo completeness and progression must be duration, intensity, then additional symptoms.");
        }

        if (package.Pathway == ClinicalPathways.AbdominalPain &&
            definition.PrimarySymptomDisplayLabel != "Stomach pain")
        {
            throw Invalid("ABDOMINAL_PAIN must use the demo display label 'Stomach pain'.");
        }
    }

    private static IReadOnlyDictionary<QuestionCode, ClinicalQuestionDefinition> ValidateQuestions(
        ClinicalDefinitionPackage package)
    {
        var questions = package.Questions.ToDictionaryOrInvalid(
            question => question.Code,
            "Question codes must be unique within a questionnaire version.");
        if (questions.Count == 0)
        {
            throw Invalid("A clinical questionnaire must contain questions.");
        }

        if (package.Questions.Select(question => question.DisplayOrder).Distinct().Count() !=
            package.Questions.Count)
        {
            throw Invalid("Question display order must be unique within a version.");
        }

        var persistedCodes = package.Questionnaire.Questions
            .Select(question => question.Code)
            .ToHashSet();
        if (!persistedCodes.SetEquals(questions.Keys))
        {
            throw Invalid("Structured and persisted questionnaire questions must match.");
        }

        foreach (var question in package.Questions)
        {
            if (question.Answer.AllowedValues is { Count: > 0 } allowedValues &&
                allowedValues.Distinct(StringComparer.Ordinal).Count() != allowedValues.Count)
            {
                throw Invalid($"Question '{question.Code}' has duplicate answer values.");
            }

            if (question.Answer.Minimum > question.Answer.Maximum)
            {
                throw Invalid($"Question '{question.Code}' has an invalid answer range.");
            }
        }

        return questions;
    }

    private static void ValidateBranches(
        IReadOnlyList<ClinicalBranchDefinition> branches,
        IReadOnlyDictionary<QuestionCode, ClinicalQuestionDefinition> questions)
    {
        if (branches.Select(branch => branch.Code).Distinct(StringComparer.Ordinal).Count() !=
            branches.Count)
        {
            throw Invalid("Branch codes must be unique within a package version.");
        }

        foreach (var branch in branches)
        {
            if (!questions.TryGetValue(branch.TriggerQuestionCode, out var trigger))
            {
                throw Invalid($"Branch '{branch.Code}' references an unknown trigger question.");
            }

            if (branch.ExpectedValues.Count == 0 || branch.NextQuestionCodes.Count == 0)
            {
                throw Invalid($"Branch '{branch.Code}' requires values and next questions.");
            }

            foreach (var nextQuestionCode in branch.NextQuestionCodes)
            {
                if (!questions.ContainsKey(nextQuestionCode))
                {
                    throw Invalid(
                        $"Branch '{branch.Code}' references unknown question '{nextQuestionCode}'.");
                }
            }

            ValidateExpectedValues(branch, trigger);
        }
    }

    private static void ValidateExpectedValues(
        ClinicalBranchDefinition branch,
        ClinicalQuestionDefinition trigger)
    {
        if (branch.Operator == ClinicalConditionOperator.ClassifiedAs)
        {
            return;
        }

        if (trigger.Answer.Type == ClinicalAnswerType.Boolean &&
            branch.ExpectedValues.Any(value => value is not "TRUE" and not "FALSE"))
        {
            throw Invalid($"Branch '{branch.Code}' has an invalid boolean answer value.");
        }

        if (trigger.Answer.AllowedValues is { Count: > 0 } allowedValues &&
            branch.ExpectedValues.Any(value => !allowedValues.Contains(value, StringComparer.Ordinal)))
        {
            throw Invalid($"Branch '{branch.Code}' references an invalid answer value.");
        }

        if (branch.Operator is ClinicalConditionOperator.GreaterThan or
            ClinicalConditionOperator.GreaterThanOrEqual &&
            branch.ExpectedValues.Any(value =>
                !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)))
        {
            throw Invalid($"Branch '{branch.Code}' requires numeric expected values.");
        }
    }

    private static void ValidateRulePackage(
        ClinicalRulePackageDefinition definitions,
        IReadOnlyDictionary<QuestionCode, ClinicalQuestionDefinition> questions)
    {
        var urgencyRanks = definitions.Urgencies.ToDictionaryOrInvalid(
            urgency => urgency.Code,
            "Urgency codes must be unique.");
        if (urgencyRanks.Count != ClinicalUrgencies.SeverityOrder.Count ||
            ClinicalUrgencies.SeverityOrder.Any(expected =>
                !urgencyRanks.TryGetValue(expected.Key, out var actual) ||
                actual.SeverityRank != expected.Value))
        {
            throw Invalid("Urgency vocabulary or deterministic severity ordering is invalid.");
        }

        if (definitions.Urgencies.Any(urgency =>
            string.IsNullOrWhiteSpace(urgency.Description)))
        {
            throw Invalid("Every urgency requires a definition.");
        }

        if (definitions.Dispositions.Count != urgencyRanks.Count ||
            definitions.Dispositions.Select(value => value.Code)
                .Distinct(StringComparer.Ordinal).Count() != definitions.Dispositions.Count ||
            definitions.Dispositions.Select(value => value.ForUrgency).Distinct().Count() !=
                definitions.Dispositions.Count ||
            definitions.Dispositions.Any(value =>
                !urgencyRanks.ContainsKey(value.ForUrgency) ||
                string.IsNullOrWhiteSpace(value.Code) ||
                string.IsNullOrWhiteSpace(value.Recommendation)))
        {
            throw Invalid(
                "Every urgency requires one separate valid disposition recommendation.");
        }

        if (definitions.RedFlags.Select(value => value.Code)
                .Distinct(StringComparer.Ordinal).Count() != definitions.RedFlags.Count ||
            definitions.Rules.Select(value => value.Code)
                .Distinct(StringComparer.Ordinal).Count() != definitions.Rules.Count)
        {
            throw Invalid("Red-flag and rule codes must be unique.");
        }

        foreach (var redFlag in definitions.RedFlags)
        {
            ValidateConditions(redFlag.Code, redFlag.AllOf, questions);
            ValidateConditions(redFlag.Code, redFlag.AnyOf, questions);
        }

        foreach (var rule in definitions.Rules)
        {
            if (!ClinicalUrgencies.SeverityOrder.ContainsKey(rule.MinimumUrgency))
            {
                throw Invalid($"Rule '{rule.Code}' has an invalid urgency.");
            }

            ValidateConditions(rule.Code, rule.AllOf, questions);
            ValidateConditions(rule.Code, rule.AnyOf, questions);
        }

        if (definitions.ClinicalLimitations.Count == 0)
        {
            throw Invalid("The provisional package must preserve its clinical limitations.");
        }
    }

    private static void ValidateConditions(
        string ownerCode,
        IReadOnlyList<ClinicalConditionDefinition>? conditions,
        IReadOnlyDictionary<QuestionCode, ClinicalQuestionDefinition> questions)
    {
        if (conditions is null)
        {
            return;
        }

        foreach (var condition in conditions)
        {
            if (!questions.ContainsKey(condition.FactCode))
            {
                throw Invalid(
                    $"Definition '{ownerCode}' references unknown fact '{condition.FactCode}'.");
            }

            if (string.IsNullOrWhiteSpace(condition.ExpectedValue))
            {
                throw Invalid($"Definition '{ownerCode}' has an empty expected value.");
            }
        }
    }

    private static ClinicalDefinitionValidationException Invalid(string message)
    {
        return new ClinicalDefinitionValidationException(message);
    }
}

internal static class ClinicalDefinitionValidationDictionaryExtensions
{
    public static IReadOnlyDictionary<TKey, TValue> ToDictionaryOrInvalid<TValue, TKey>(
        this IEnumerable<TValue> source,
        Func<TValue, TKey> keySelector,
        string error)
        where TKey : notnull
    {
        try
        {
            return source.ToDictionary(keySelector);
        }
        catch (ArgumentException exception)
        {
            throw new ClinicalDefinitionValidationException(error) { Source = exception.Source };
        }
    }
}
