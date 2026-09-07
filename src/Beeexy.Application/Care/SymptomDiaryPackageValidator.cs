using System.Text.Json;
using Beeexy.Domain.Care;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Care;

public sealed class SymptomDiaryPackageValidator
{
    private const int MaximumTextLength = 4000;
    private const int MaximumBodyLength = 65536;
    private const int MaximumJsonLength = 65536;
    private const int MaximumReferenceLength = 500;

    public void Validate(SymptomDiaryPackageDefinition package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Required(package.PackageCode, "A package code is required.");
        Required(package.PackageVersion, "A package version is required.");
        Required(package.QuestionSetCode, "A question-set code is required.");
        Required(package.QuestionSetVersion, "A question-set version is required.");
        Required(package.SymptomInformationCode, "An information code is required.");
        Required(package.SymptomInformationVersion, "An information version is required.");
        Required(package.Pathway, "A pathway is required.");
        Required(package.ContentStatus, "Content provenance is required.");
        if (!ClinicalPathways.Recognized.Contains(package.Pathway))
        {
            throw Invalid("The symptom-diary pathway is not recognized.");
        }

        ValidateProvenance(package);
        ValidateText(package.InformationalHeading, MaximumTextLength, "informational heading", true);
        ValidateText(package.InformationalBody, MaximumBodyLength, "informational body", true);
        ValidateQuestions(package.Questions);
        ValidateWarningSigns(package.WarningSigns);
    }

    public bool IsDisplayEligible(SymptomDiaryPackageDefinition package)
    {
        Validate(package);
        return package.ContentStatus.ReviewStatus == ClinicalReviewStatus.Reviewed &&
            package.ContentStatus.ApprovalStatus == ClinicalApprovalStatus.Approved &&
            package.ApprovedAt.HasValue &&
            package.ActivatedAt.HasValue;
    }

    private static void ValidateProvenance(SymptomDiaryPackageDefinition package)
    {
        if (!Enum.IsDefined(package.ContentStatus.Source) ||
            !Enum.IsDefined(package.ContentStatus.ReviewStatus) ||
            !Enum.IsDefined(package.ContentStatus.ApprovalStatus) ||
            package.ContentStatus.Source == ClinicalContentSource.LegacyUnspecified)
        {
            throw Invalid("A truthful non-legacy clinical-content source is required.");
        }

        ValidateText(package.SourceReference, MaximumReferenceLength, "source reference");
        EnsureUtc(package.ImportedAt, "import timestamp");
        if (package.ApprovedAt.HasValue)
        {
            EnsureUtc(package.ApprovedAt.Value, "approval timestamp");
        }

        if (package.ActivatedAt.HasValue)
        {
            EnsureUtc(package.ActivatedAt.Value, "activation timestamp");
        }

        var approved = package.ContentStatus.ApprovalStatus == ClinicalApprovalStatus.Approved;
        var reviewed = package.ContentStatus.ReviewStatus == ClinicalReviewStatus.Reviewed;
        var provisional = package.ContentStatus.ApprovalStatus ==
                ClinicalApprovalStatus.PendingFormalReview &&
            package.ContentStatus.ReviewStatus == ClinicalReviewStatus.Provisional;
        var nonClinical = package.ContentStatus.ApprovalStatus ==
                ClinicalApprovalStatus.NotClinicallyApproved &&
            package.ContentStatus.ReviewStatus == ClinicalReviewStatus.NotApplicable;
        if (!(approved && reviewed || provisional || nonClinical) ||
            package.ApprovedAt.HasValue != approved)
        {
            throw Invalid("Review, approval status, and approval timestamp do not agree.");
        }

        if (package.ApprovedAt.HasValue && package.ApprovedAt.Value < package.ImportedAt)
        {
            throw Invalid("The approval timestamp cannot precede the import timestamp.");
        }

        if (package.ContentStatus.Source == ClinicalContentSource.ProductDemoDefined &&
            !nonClinical)
        {
            throw Invalid("Product-demo content cannot be represented as clinically approved.");
        }

        if (!package.ActivatedAt.HasValue)
        {
            return;
        }

        if (!approved || !reviewed || package.ActivatedAt.Value < package.ImportedAt ||
            package.ActivatedAt.Value < package.ApprovedAt!.Value)
        {
            throw Invalid("Only reviewed and approved content can have valid activation provenance.");
        }
    }

    private static void ValidateQuestions(
        IReadOnlyList<SymptomDiaryQuestionDefinition> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
        {
            throw Invalid("A symptom-diary package requires at least one question.");
        }

        EnsureUnique(
            questions,
            value => Required(value?.Code, "Every question requires a code."),
            "Question codes must be unique within a package.");
        EnsureUnique(
            questions,
            value => PositiveOrder(value?.SourceOrder, "question"),
            "Question source order must be unique within a package.");
        foreach (var question in questions)
        {
            ArgumentNullException.ThrowIfNull(question);
            ValidateText(question.PromptText, MaximumTextLength, "question prompt");
            ValidateAnswerSchema(question.AnswerSchemaJson);
            ValidateOptions(question.Options);
        }
    }

    private static void ValidateOptions(
        IReadOnlyList<SymptomDiaryQuestionOptionDefinition> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureUnique(
            options,
            value => Required(value?.Code, "Every option requires a code."),
            "Option codes must be unique within a question.");
        EnsureUnique(
            options,
            value => PositiveOrder(value?.SourceOrder, "option"),
            "Option source order must be unique within a question.");
        foreach (var option in options)
        {
            ArgumentNullException.ThrowIfNull(option);
            ValidateText(option.Value, MaximumTextLength, "option value");
            ValidateText(option.DisplayText, MaximumTextLength, "option display text");
        }
    }

    private static void ValidateWarningSigns(
        IReadOnlyList<SymptomWarningSignDefinition> warningSigns)
    {
        ArgumentNullException.ThrowIfNull(warningSigns);
        EnsureUnique(
            warningSigns,
            value => Required(value?.Code, "Every warning sign requires a code."),
            "Warning-sign codes must be unique within a package.");
        EnsureUnique(
            warningSigns,
            value => PositiveOrder(value?.SourceOrder, "warning sign"),
            "Warning-sign source order must be unique within a package.");
        foreach (var warningSign in warningSigns)
        {
            ArgumentNullException.ThrowIfNull(warningSign);
            ValidateText(warningSign.DisplayText, MaximumTextLength, "warning-sign display text");
        }
    }

    private static void ValidateAnswerSchema(string json)
    {
        ValidateText(json, MaximumJsonLength, "answer schema");
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("An answer schema must be a JSON object.");
            }

            EnsureNoDuplicateProperties(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new SymptomDiaryPackageValidationException(
                $"The answer schema is malformed JSON: {exception.Message}");
        }
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw Invalid("An answer schema cannot contain duplicate property names.");
                }

                EnsureNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureNoDuplicateProperties(item);
            }
        }
    }

    private static T Required<T>(T? value, string message) where T : class
        => value ?? throw Invalid(message);

    private static int PositiveOrder(int? value, string owner)
    {
        if (!value.HasValue || value.Value <= 0)
        {
            throw Invalid($"Every {owner} requires positive source order.");
        }

        return value.Value;
    }

    private static void EnsureUnique<TItem, TKey>(
        IEnumerable<TItem> values,
        Func<TItem, TKey> keySelector,
        string message)
        where TKey : notnull
    {
        var keys = new HashSet<TKey>();
        foreach (var value in values)
        {
            if (!keys.Add(keySelector(value)))
            {
                throw Invalid(message);
            }
        }
    }

    private static void ValidateText(
        string? value,
        int maximumLength,
        string field,
        bool optional = false)
    {
        if (value is null && optional)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw Invalid($"The {field} is missing or exceeds its persistence limit.");
        }
    }

    private static void EnsureUtc(DateTimeOffset value, string field)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw Invalid($"The {field} must be UTC.");
        }
    }

    private static SymptomDiaryPackageValidationException Invalid(string message) => new(message);
}
