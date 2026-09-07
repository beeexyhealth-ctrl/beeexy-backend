using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Beeexy.Application.Care;
using Beeexy.Domain.Triage;

namespace Beeexy.Infrastructure.Care;

public sealed class SymptomDiaryPackageCanonicalSerializer
{
    public string Serialize(SymptomDiaryPackageDefinition package)
    {
        ArgumentNullException.ThrowIfNull(package);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions
                   {
                       Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                       Indented = false
                   }))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", "beeexy.symptom-diary-package.v1");
            writer.WriteString("packageCode", package.PackageCode.Value);
            writer.WriteString("packageVersion", package.PackageVersion.Value);
            writer.WriteString("questionSetCode", package.QuestionSetCode.Value);
            writer.WriteString("questionSetVersion", package.QuestionSetVersion.Value);
            writer.WriteString("symptomInformationCode", package.SymptomInformationCode.Value);
            writer.WriteString("symptomInformationVersion", package.SymptomInformationVersion.Value);
            writer.WriteString("pathway", package.Pathway.Value);
            WriteProvenance(writer, package);
            WriteNullableString(writer, "informationalHeading", package.InformationalHeading);
            WriteNullableString(writer, "informationalBody", package.InformationalBody);
            WriteQuestions(writer, package.Questions);
            WriteWarningSigns(writer, package.WarningSigns);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteProvenance(
        Utf8JsonWriter writer,
        SymptomDiaryPackageDefinition package)
    {
        writer.WriteStartObject("provenance");
        writer.WriteString("source", SerializeSource(package.ContentStatus.Source));
        writer.WriteString("reviewStatus", SerializeReviewStatus(package.ContentStatus.ReviewStatus));
        writer.WriteString(
            "approvalStatus",
            SerializeApprovalStatus(package.ContentStatus.ApprovalStatus));
        writer.WriteString("sourceReference", package.SourceReference);
        WriteNullableInstant(writer, "approvedAt", package.ApprovedAt);
        WriteNullableInstant(writer, "activatedAt", package.ActivatedAt);
        writer.WriteEndObject();
    }

    private static void WriteQuestions(
        Utf8JsonWriter writer,
        IReadOnlyList<SymptomDiaryQuestionDefinition> questions)
    {
        writer.WriteStartArray("questions");
        foreach (var question in questions.OrderBy(value => value.SourceOrder))
        {
            writer.WriteStartObject();
            writer.WriteString("code", question.Code.Value);
            writer.WriteString("promptText", question.PromptText);
            writer.WriteNumber("sourceOrder", question.SourceOrder);
            writer.WriteBoolean("isRequired", question.IsRequired);
            writer.WritePropertyName("answerSchema");
            using (var schema = JsonDocument.Parse(question.AnswerSchemaJson))
            {
                WriteCanonicalJson(schema.RootElement, writer);
            }

            writer.WriteStartArray("options");
            foreach (var option in question.Options.OrderBy(value => value.SourceOrder))
            {
                writer.WriteStartObject();
                writer.WriteString("code", option.Code.Value);
                writer.WriteString("value", option.Value);
                writer.WriteString("displayText", option.DisplayText);
                writer.WriteNumber("sourceOrder", option.SourceOrder);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteWarningSigns(
        Utf8JsonWriter writer,
        IReadOnlyList<SymptomWarningSignDefinition> warningSigns)
    {
        writer.WriteStartArray("warningSigns");
        foreach (var warningSign in warningSigns.OrderBy(value => value.SourceOrder))
        {
            writer.WriteStartObject();
            writer.WriteString("code", warningSign.Code.Value);
            writer.WriteString("displayText", warningSign.DisplayText);
            writer.WriteNumber("sourceOrder", warningSign.SourceOrder);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteCanonicalJson(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(
                             value => value.Name,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(item, writer);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new SymptomDiaryPackageValidationException(
                    "The answer schema contains an unsupported JSON value.");
        }
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteNullableInstant(
        Utf8JsonWriter writer,
        string name,
        DateTimeOffset? value)
    {
        if (!value.HasValue)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteString(
            name,
            value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }

    private static string SerializeSource(ClinicalContentSource value) => value switch
    {
        ClinicalContentSource.LegacyUnspecified => "LEGACY_UNSPECIFIED",
        ClinicalContentSource.ReferencePlatformDerived => "REFERENCE_PLATFORM_DERIVED",
        ClinicalContentSource.ProductDemoDefined => "PRODUCT_DEMO_DEFINED",
        ClinicalContentSource.MedicalTeamProvided => "MEDICAL_TEAM_PROVIDED",
        _ => throw new SymptomDiaryPackageValidationException("Unknown content source.")
    };

    private static string SerializeReviewStatus(ClinicalReviewStatus value) => value switch
    {
        ClinicalReviewStatus.Reviewed => "REVIEWED",
        ClinicalReviewStatus.Provisional => "PROVISIONAL",
        ClinicalReviewStatus.NotApplicable => "NOT_APPLICABLE",
        _ => throw new SymptomDiaryPackageValidationException("Unknown review status.")
    };

    private static string SerializeApprovalStatus(ClinicalApprovalStatus value) => value switch
    {
        ClinicalApprovalStatus.Approved => "APPROVED",
        ClinicalApprovalStatus.PendingFormalReview => "PENDING_FORMAL_REVIEW",
        ClinicalApprovalStatus.NotClinicallyApproved => "NOT_CLINICALLY_APPROVED",
        _ => throw new SymptomDiaryPackageValidationException("Unknown approval status.")
    };
}
