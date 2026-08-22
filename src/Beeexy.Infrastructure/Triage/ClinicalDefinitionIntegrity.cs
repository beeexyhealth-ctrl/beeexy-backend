using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beeexy.Domain.Triage;

namespace Beeexy.Infrastructure.Triage;

internal static class ClinicalDefinitionIntegrity
{
    public static DefinitionHash QuestionnaireHash(IEnumerable<TriageQuestionInput> questions)
    {
        return Hash(string.Join(
            "\u001e",
            questions.OrderBy(value => value.DisplayOrder).Select(value => string.Join(
                "\u001f",
                value.Code.Value,
                value.PromptText,
                value.DisplayOrder,
                CanonicalJson(value.AnswerSchemaJson),
                CanonicalJson(value.BranchingMetadataJson)))));
    }

    public static DefinitionHash QuestionnaireHash(IEnumerable<TriageQuestion> questions)
    {
        return Hash(string.Join(
            "\u001e",
            questions.OrderBy(value => value.DisplayOrder).Select(value => string.Join(
                "\u001f",
                value.Code.Value,
                value.PromptText,
                value.DisplayOrder,
                CanonicalJson(value.AnswerSchemaJson),
                CanonicalJson(value.BranchingMetadataJson)))));
    }

    public static DefinitionHash RulePackageHash(string json)
    {
        return Hash(CanonicalJson(json));
    }

    private static string CanonicalJson(string? json)
    {
        if (json is null)
        {
            return string.Empty;
        }

        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(document.RootElement, writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
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
                    WriteCanonical(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(item, writer);
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
                throw new InvalidOperationException("Unsupported JSON value in clinical definition.");
        }
    }

    private static DefinitionHash Hash(string value)
    {
        return DefinitionHash.FromHash(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant());
    }
}
