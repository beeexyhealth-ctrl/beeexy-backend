using System.Text.Json;

namespace Beeexy.Application.Ai;

public enum AiStructuralValidationIssue
{
    UnsupportedSchema,
    InvalidJson,
    InvalidRootType,
    MissingRequiredField,
    InvalidFieldType,
    SchemaVersionMismatch,
    InvalidStructure
}

public sealed record AiStructuralValidationResult(
    bool IsValid,
    AiStructuralValidationIssue? Issue = null)
{
    public static AiStructuralValidationResult Valid { get; } = new(true);

    public static AiStructuralValidationResult Invalid(AiStructuralValidationIssue issue)
    {
        if (!Enum.IsDefined(issue))
        {
            throw new ArgumentOutOfRangeException(nameof(issue));
        }

        return new AiStructuralValidationResult(false, issue);
    }
}

public interface IAiStructuredResultSchema
{
    AiStructuredResultIdentity Identity { get; }

    AiStructuralValidationResult Validate(JsonElement result);
}

public interface IAiStructuredResultValidator
{
    AiStructuralValidationResult Validate(
        AiStructuredResultIdentity identity,
        string structuredContent);
}

public sealed class AiStructuredResultValidator : IAiStructuredResultValidator
{
    private readonly IReadOnlyDictionary<AiStructuredResultIdentity, IAiStructuredResultSchema>
        schemas;

    public AiStructuredResultValidator(IEnumerable<IAiStructuredResultSchema> schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        this.schemas = schemas.ToDictionary(schema => schema.Identity);
    }

    public AiStructuralValidationResult Validate(
        AiStructuredResultIdentity identity,
        string structuredContent)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!schemas.TryGetValue(identity, out var schema))
        {
            return AiStructuralValidationResult.Invalid(
                AiStructuralValidationIssue.UnsupportedSchema);
        }

        if (string.IsNullOrWhiteSpace(structuredContent))
        {
            return AiStructuralValidationResult.Invalid(
                AiStructuralValidationIssue.InvalidJson);
        }

        try
        {
            using var document = JsonDocument.Parse(structuredContent);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return AiStructuralValidationResult.Invalid(
                    AiStructuralValidationIssue.InvalidRootType);
            }

            return schema.Validate(document.RootElement);
        }
        catch (JsonException)
        {
            return AiStructuralValidationResult.Invalid(
                AiStructuralValidationIssue.InvalidJson);
        }
    }
}
