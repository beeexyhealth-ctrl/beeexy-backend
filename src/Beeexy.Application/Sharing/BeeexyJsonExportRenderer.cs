using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beeexy.Domain.Common;

namespace Beeexy.Application.Sharing;

public sealed class BeeexyJsonExportRenderer
{
    public const string FormatVersion = "1.0";
    public const string SnapshotVersion = "canonical-shared-health-v1";
    public const string MediaType = "application/vnd.beeexy.health-snapshot+json";

    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public byte[] Render(
        CanonicalSharedHealthSnapshot snapshot,
        EntityId snapshotId,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshotId.Value == Guid.Empty)
        {
            throw new ArgumentException("A snapshot identifier is required.", nameof(snapshotId));
        }

        if (generatedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The generation time must be UTC.", nameof(generatedAt));
        }

        return JsonSerializer.SerializeToUtf8Bytes(
            new BeeexyJsonExportDocument(
                "BeeexyJson",
                FormatVersion,
                generatedAt,
                snapshotId.Value,
                SnapshotVersion,
                ToProfile(snapshot)),
            SerializerOptions);
    }

    private static BeeexyJsonHealthProfile ToProfile(CanonicalSharedHealthSnapshot value) => new(
        value.Demographics is null
            ? null
            : new BeeexyJsonDemographics(
                value.Demographics.BeeexyId,
                value.Demographics.FirstName,
                value.Demographics.LastName,
                value.Demographics.DateOfBirth,
                value.Demographics.SexAssignedAtBirth,
                value.Demographics.State,
                value.Demographics.Version),
        value.ClinicalHistory.Select(item => new BeeexyJsonClinicalHistoryEvent(
            item.EventId.Value,
            item.EventType,
            item.OccurredAt,
            item.RecordedAt,
            new BeeexyJsonClinicalProvenance(
                item.Provenance.SourceType,
                item.Provenance.SourceId.Value,
                item.Provenance.QuestionnaireVersionId.Value,
                item.Provenance.ClinicalRuleSetVersionId.Value))).ToArray(),
        value.PreTriage.Select(item => new BeeexyJsonPreTriageRecord(
            item.EpisodeId.Value,
            item.CompletedAt,
            new BeeexyJsonPrimarySymptom(item.PrimarySymptom.Code, item.PrimarySymptom.Display),
            new BeeexyJsonDuration(item.Duration.Value, item.Duration.Unit),
            item.Intensity,
            item.AdditionalSymptoms,
            item.QuestionnaireVersionId.Value,
            item.ClinicalRuleSetVersionId.Value)).ToArray(),
        value.SymptomDiaryEntries.Select(item => new BeeexyJsonSymptomDiaryEntry(
            item.CheckInId.Value,
            item.EpisodeId.Value,
            item.RecordedAt,
            item.Pathway,
            ToPackage(item.Package),
            item.Answers.Select(answer => new BeeexyJsonSymptomDiaryAnswer(
                answer.QuestionCode,
                answer.PromptText,
                answer.Value)).ToArray())).ToArray(),
        value.SymptomDiaryContent.Select(item => new BeeexyJsonSymptomDiaryContent(
            ToPackage(item.Package),
            item.Pathway,
            item.InformationalHeading,
            item.InformationalBody,
            item.WarningSigns.Select(warning => new BeeexyJsonWarningSign(
                warning.Code,
                warning.DisplayText,
                warning.SourceOrder)).ToArray())).ToArray(),
        value.SecondOpinions.Select(item => new BeeexyJsonSecondOpinion(
            item.ResultId.Value,
            item.GeneratedAt,
            item.ResultVersion,
            item.Summary,
            item.ImportantPoints,
            item.PossibleQuestionsForDoctor,
            item.MissingInformation,
            item.Disclaimer)).ToArray());

    private static BeeexyJsonPackageVersion ToPackage(
        SharedSymptomDiaryPackageVersion value) => new(
        value.PackageVersionId.Value,
        value.PackageCode,
        value.PackageVersion,
        value.CanonicalContentHash,
        value.QuestionSetCode,
        value.QuestionSetVersion,
        value.SymptomInformationCode,
        value.SymptomInformationVersion,
        value.ContentSource,
        value.ReviewStatus,
        value.ApprovalStatus,
        value.ApprovedAt);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false
        };
        options.Converters.Add(new UtcDateTimeOffsetJsonConverter());
        options.Converters.Add(new CanonicalJsonElementConverter());
        return options;
    }
}

public sealed class ExportArtifactChecksumCalculator
{
    public const string Algorithm = "SHA-256";

    public string Calculate(ReadOnlySpan<byte> artifactBytes) =>
        Convert.ToHexString(SHA256.HashData(artifactBytes)).ToLowerInvariant();
}

public sealed record BeeexyJsonExportDocument(
    string Format,
    string FormatVersion,
    DateTimeOffset GeneratedAt,
    Guid SnapshotId,
    string SnapshotVersion,
    BeeexyJsonHealthProfile Profile);

public sealed record BeeexyJsonHealthProfile(
    BeeexyJsonDemographics? Demographics,
    IReadOnlyList<BeeexyJsonClinicalHistoryEvent> ClinicalHistory,
    IReadOnlyList<BeeexyJsonPreTriageRecord> PreTriage,
    IReadOnlyList<BeeexyJsonSymptomDiaryEntry> SymptomDiaryEntries,
    IReadOnlyList<BeeexyJsonSymptomDiaryContent> SymptomDiaryContent,
    IReadOnlyList<BeeexyJsonSecondOpinion> SecondOpinions);

public sealed record BeeexyJsonDemographics(
    string BeeexyId,
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    string? SexAssignedAtBirth,
    string? State,
    long Version);

public sealed record BeeexyJsonClinicalHistoryEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    BeeexyJsonClinicalProvenance Provenance);

public sealed record BeeexyJsonClinicalProvenance(
    string SourceType,
    Guid SourceId,
    Guid QuestionnaireVersionId,
    Guid ClinicalRuleSetVersionId);

public sealed record BeeexyJsonPreTriageRecord(
    Guid EpisodeId,
    DateTimeOffset CompletedAt,
    BeeexyJsonPrimarySymptom PrimarySymptom,
    BeeexyJsonDuration Duration,
    int Intensity,
    IReadOnlyList<string> AdditionalSymptoms,
    Guid QuestionnaireVersionId,
    Guid ClinicalRuleSetVersionId);

public sealed record BeeexyJsonPrimarySymptom(string Code, string Display);

public sealed record BeeexyJsonDuration(decimal Value, string Unit);

public sealed record BeeexyJsonSymptomDiaryEntry(
    Guid CheckInId,
    Guid EpisodeId,
    DateTimeOffset RecordedAt,
    string Pathway,
    BeeexyJsonPackageVersion Package,
    IReadOnlyList<BeeexyJsonSymptomDiaryAnswer> Answers);

public sealed record BeeexyJsonSymptomDiaryAnswer(
    string QuestionCode,
    string PromptText,
    JsonElement Value);

public sealed record BeeexyJsonSymptomDiaryContent(
    BeeexyJsonPackageVersion Package,
    string Pathway,
    string? InformationalHeading,
    string? InformationalBody,
    IReadOnlyList<BeeexyJsonWarningSign> WarningSigns);

public sealed record BeeexyJsonPackageVersion(
    Guid PackageVersionId,
    string PackageCode,
    string PackageVersion,
    string CanonicalContentHash,
    string QuestionSetCode,
    string QuestionSetVersion,
    string SymptomInformationCode,
    string SymptomInformationVersion,
    string ContentSource,
    string ReviewStatus,
    string ApprovalStatus,
    DateTimeOffset ApprovedAt);

public sealed record BeeexyJsonWarningSign(
    string Code,
    string DisplayText,
    int SourceOrder);

public sealed record BeeexyJsonSecondOpinion(
    Guid ResultId,
    DateTimeOffset GeneratedAt,
    string ResultVersion,
    string Summary,
    IReadOnlyList<string> ImportantPoints,
    IReadOnlyList<string> PossibleQuestionsForDoctor,
    IReadOnlyList<string> MissingInformation,
    string Disclaimer);

internal sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => throw new NotSupportedException();

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            System.Globalization.CultureInfo.InvariantCulture));
    }
}

internal sealed class CanonicalJsonElementConverter : JsonConverter<JsonElement>
{
    public override JsonElement Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => JsonElement.ParseValue(ref reader);

    public override void Write(
        Utf8JsonWriter writer,
        JsonElement value,
        JsonSerializerOptions options) => WriteCanonical(writer, value);

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(
                             item => item.Name,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
