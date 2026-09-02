using System.Text.Json;
using Beeexy.Application.Common;
using Beeexy.Domain.Ai;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Ai;

public static class SecondOpinionOptions
{
    public const int MaximumTypedTextCharacters = 8_000;
    public const int MaximumDocumentTextCharacters = 64_000;
    public const int MaximumClinicalHistoryEvents = 3;
}

public sealed record SecondOpinionProductContent
{
    public const string DisclaimerVersion = "ai-second-opinion-disclaimer-v1";
    public const string ResultVersion = "ai-second-opinion-result@v1";
    public const string Disclaimer =
        "This is not a medical diagnosis. Beeexy AI offers educational insights based on " +
        "clinical literature, not a substitute for a licensed physician. Always discuss " +
        "results with your doctor.";

    public static SecondOpinionProductContent Current { get; } = new();
}

public sealed record RequestSecondOpinionCommand(
    EntityId PatientProfileId,
    string? Text,
    IReadOnlyList<EntityId>? DocumentIds,
    EntityId? PreTriageSessionId,
    IReadOnlyList<EntityId>? ClinicalHistoryEventIds,
    string CorrelationIdentifier);

public sealed record RegenerateSecondOpinionCommand(
    EntityId AnalysisId,
    string CorrelationIdentifier);

public sealed record SecondOpinionPreparedInput(
    string ProviderInputJson,
    string ImmutableInputJson,
    AiUploadedDocument? Document);

public interface ISecondOpinionInputAssembler
{
    Task<SecondOpinionPreparedInput> AssembleAsync(
        RequestSecondOpinionCommand command,
        EntityId accountId,
        CancellationToken cancellationToken = default);
}

public enum SecondOpinionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Rejected
}

public sealed record SecondOpinionRequestReceipt(
    EntityId AnalysisId,
    EntityId ExecutionId,
    SecondOpinionStatus Status);

public sealed record SecondOpinionResult(
    string Summary,
    IReadOnlyList<string> ImportantPoints,
    IReadOnlyList<string> PossibleQuestionsForDoctor,
    IReadOnlyList<string> MissingInformation,
    string Disclaimer);

public sealed record SecondOpinionMetadata(
    bool AiGenerated,
    DateTimeOffset GeneratedAt,
    string ResultVersion,
    string? Provider,
    string? ModelVersion,
    string? PromptVersion,
    string DisclaimerVersion);

public sealed record SecondOpinionDetail(
    EntityId AnalysisId,
    EntityId PatientProfileId,
    EntityId? ExecutionId,
    SecondOpinionStatus Status,
    SecondOpinionResult? Result,
    SecondOpinionMetadata? Metadata,
    string? SafeMessage);

public sealed record SecondOpinionStoredState(
    AiExecutionStatus? ExecutionStatus,
    EntityId? ExecutionId,
    string? ProviderIdentifier,
    string? ModelIdentifier,
    string? PromptVersion,
    string? ResultContentJson,
    DateTimeOffset? ResultCreatedAt,
    AiSafetyCategory? SafetyCategory,
    bool? DisplayEligible,
    string? ProductContentVersion);

public sealed record SecondOpinionAnalysisAccess(
    EntityId AnalysisId,
    EntityId PatientProfileId);

public sealed record SecondOpinionRegenerationSource(
    EntityId AnalysisId,
    EntityId PatientProfileId,
    string OriginalInputSchemaVersion,
    string OriginalInputSnapshotJson,
    int NextSnapshotSequence);

public interface ISecondOpinionExecutionLease : IAsyncDisposable;

public interface ISecondOpinionRepository
{
    void Add(AiAnalysisRequest request);

    Task<SecondOpinionAnalysisAccess?> FindOwnedAsync(
        EntityId analysisId,
        EntityId accountId,
        CancellationToken cancellationToken = default);

    Task<SecondOpinionRegenerationSource?> FindRegenerationSourceAsync(
        EntityId analysisId,
        EntityId accountId,
        CancellationToken cancellationToken = default);

    Task<ISecondOpinionExecutionLease?> TryAcquireExecutionLeaseAsync(
        EntityId analysisId,
        CancellationToken cancellationToken = default);

    Task<SecondOpinionStoredState> GetStateAsync(
        EntityId analysisId,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class SecondOpinionNotFoundException : Exception
{
    public SecondOpinionNotFoundException()
        : base("The requested Second Opinion could not be found.")
    {
    }
}

public sealed class SecondOpinionExecutionConflictException : Exception
{
    public SecondOpinionExecutionConflictException()
        : base("Another execution is already running for this Second Opinion.")
    {
    }
}

public static class SecondOpinionImmutableInput
{
    public const string SchemaVersion = "ai-second-opinion-input@v1";

    private static readonly string[] InputProperties =
    [
        "demographics",
        "typedText",
        "document",
        "preTriage",
        "clinicalHistory"
    ];

    private static readonly string[] ProvenanceProperties =
    [
        "patientId",
        "documentId",
        "preTriageSessionId",
        "clinicalHistoryEventIds"
    ];

    public static string ReplayProviderInput(
        string originalInputSchemaVersion,
        string originalInputSnapshotJson)
    {
        try
        {
            if (!string.Equals(
                    originalInputSchemaVersion,
                    SchemaVersion,
                    StringComparison.Ordinal))
            {
                throw Invalid();
            }

            using var document = JsonDocument.Parse(originalInputSnapshotJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasExactProperties(root, "schemaVersion", "input", "provenance") ||
                root.GetProperty("schemaVersion").ValueKind != JsonValueKind.String ||
                !string.Equals(
                    root.GetProperty("schemaVersion").GetString(),
                    "v1",
                    StringComparison.Ordinal) ||
                root.GetProperty("input").ValueKind != JsonValueKind.Object ||
                root.GetProperty("provenance").ValueKind != JsonValueKind.Object)
            {
                throw Invalid();
            }

            var input = root.GetProperty("input");
            var provenance = root.GetProperty("provenance");
            ValidateInput(input);
            ValidateProvenance(input, provenance);
            return input.GetRawText();
        }
        catch (RequestValidationException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw Invalid();
        }
    }

    private static bool HasExactProperties(JsonElement value, params string[] expected)
    {
        var properties = value.EnumerateObject().Select(property => property.Name).ToArray();
        return properties.Length == expected.Length &&
            expected.All(name => properties.Contains(name, StringComparer.Ordinal));
    }

    private static void ValidateInput(JsonElement input)
    {
        if (!HasExactProperties(input, InputProperties) ||
            !IsDemographics(input.GetProperty("demographics")) ||
            !IsOptionalMeaningfulText(
                input.GetProperty("typedText"),
                SecondOpinionOptions.MaximumTypedTextCharacters) ||
            !IsOptionalDocument(input.GetProperty("document")) ||
            !IsOptionalObject(input.GetProperty("preTriage")) ||
            !IsClinicalHistory(input.GetProperty("clinicalHistory")))
        {
            throw Invalid();
        }

        if (input.GetProperty("typedText").ValueKind == JsonValueKind.Null &&
            input.GetProperty("document").ValueKind == JsonValueKind.Null &&
            input.GetProperty("preTriage").ValueKind == JsonValueKind.Null &&
            input.GetProperty("clinicalHistory").GetArrayLength() == 0)
        {
            throw Invalid();
        }
    }

    private static void ValidateProvenance(JsonElement input, JsonElement provenance)
    {
        if (!HasExactProperties(provenance, ProvenanceProperties) ||
            !IsUuid(provenance.GetProperty("patientId")) ||
            !IsOptionalUuid(provenance.GetProperty("documentId")) ||
            !IsOptionalUuid(provenance.GetProperty("preTriageSessionId")))
        {
            throw Invalid();
        }

        var historyIds = provenance.GetProperty("clinicalHistoryEventIds");
        if (historyIds.ValueKind != JsonValueKind.Array ||
            historyIds.GetArrayLength() > SecondOpinionOptions.MaximumClinicalHistoryEvents ||
            historyIds.EnumerateArray().Any(value => !IsUuid(value)) ||
            historyIds.EnumerateArray().Select(value => value.GetGuid()).Distinct().Count() !=
                historyIds.GetArrayLength() ||
            historyIds.GetArrayLength() !=
                input.GetProperty("clinicalHistory").GetArrayLength())
        {
            throw Invalid();
        }

        var hasDocument = input.GetProperty("document").ValueKind != JsonValueKind.Null;
        var hasDocumentId = provenance.GetProperty("documentId").ValueKind != JsonValueKind.Null;
        var hasPreTriage = input.GetProperty("preTriage").ValueKind != JsonValueKind.Null;
        var hasPreTriageId =
            provenance.GetProperty("preTriageSessionId").ValueKind != JsonValueKind.Null;
        if (hasDocument != hasDocumentId || hasPreTriage != hasPreTriageId)
        {
            throw Invalid();
        }
    }

    private static bool IsDemographics(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !HasExactProperties(value, "age", "sexAssignedAtBirth"))
        {
            return false;
        }

        var age = value.GetProperty("age");
        var sex = value.GetProperty("sexAssignedAtBirth");
        return (age.ValueKind == JsonValueKind.Null ||
                age.ValueKind == JsonValueKind.Number && age.TryGetInt32(out var years) &&
                years is >= 0 and <= 150) &&
            (sex.ValueKind == JsonValueKind.Null ||
                sex.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(sex.GetString()));
    }

    private static bool IsOptionalDocument(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Object ||
            !HasExactProperties(value, "ContentType", "text"))
        {
            return false;
        }

        var contentType = value.GetProperty("ContentType");
        return contentType.ValueKind == JsonValueKind.String &&
            contentType.GetString() is "text/plain" or "application/pdf" &&
            IsRequiredMeaningfulText(
                value.GetProperty("text"),
                SecondOpinionOptions.MaximumDocumentTextCharacters);
    }

    private static bool IsOptionalMeaningfulText(JsonElement value, int maximum) =>
        value.ValueKind == JsonValueKind.Null || IsRequiredMeaningfulText(value, maximum);

    private static bool IsRequiredMeaningfulText(JsonElement value, int maximum) =>
        value.ValueKind == JsonValueKind.String &&
        value.GetString() is { } text &&
        text.Length <= maximum &&
        !string.IsNullOrWhiteSpace(text) &&
        text.Any(char.IsLetterOrDigit);

    private static bool IsOptionalObject(JsonElement value) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Object;

    private static bool IsClinicalHistory(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array &&
        value.GetArrayLength() <= SecondOpinionOptions.MaximumClinicalHistoryEvents &&
        value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object);

    private static bool IsOptionalUuid(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null || IsUuid(value);

    private static bool IsUuid(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        value.TryGetGuid(out var id) &&
        id != Guid.Empty;

    private static RequestValidationException Invalid() => new(
        "ai.second_opinion.immutable_input_invalid",
        "The original Second Opinion input is unavailable for regeneration.");
}
