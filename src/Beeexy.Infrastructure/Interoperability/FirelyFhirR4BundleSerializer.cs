using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beeexy.Application.Interoperability;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace Beeexy.Infrastructure.Interoperability;

internal sealed class FirelyFhirR4BundleSerializer : IFhirR4BundleSerializer
{
    private const string IdentityNamespace = "beeexy-fhir-r4-base-mvp-v1";

    public byte[] Serialize(FhirSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!FhirR4BaseMvp.Matches(snapshot.MappingSpecification) ||
            !snapshot.CanBeFhirValidated ||
            !snapshot.QuestionnaireResponse.CanSerializeAsFhir ||
            !snapshot.Device.CanSerializeAsFhir ||
            !snapshot.Provenance.CanSerializeAsFhir)
        {
            throw new FhirR4BundleSerializationException(
                "The snapshot is not eligible for the Beeexy FHIR R4 base mapping.");
        }

        var questionnaireResponseId = ResourceId(snapshot.ExportId.Value,
            nameof(QuestionnaireResponse));
        var deviceId = ResourceId(snapshot.ExportId.Value, nameof(Device));
        var provenanceId = ResourceId(snapshot.ExportId.Value, nameof(Provenance));
        var questionnaireResponseUrl = FullUrl(questionnaireResponseId);
        var deviceUrl = FullUrl(deviceId);

        var questionnaireResponse = MapQuestionnaireResponse(
            snapshot.QuestionnaireResponse,
            questionnaireResponseId);
        var device = MapDevice(snapshot.Device, deviceId);
        var provenance = MapProvenance(
            snapshot.Provenance,
            provenanceId,
            questionnaireResponseUrl,
            deviceUrl);
        var bundle = new Bundle
        {
            Type = Bundle.BundleType.Collection,
            Entry =
            [
                Entry(questionnaireResponseId, questionnaireResponse),
                Entry(deviceId, device),
                Entry(provenanceId, provenance)
            ]
        };

        return new FhirJsonSerializer().SerializeToBytes(bundle);
    }

    private static QuestionnaireResponse MapQuestionnaireResponse(
        QuestionnaireResponseRepresentation source,
        string resourceId)
    {
        var result = new QuestionnaireResponse
        {
            Id = resourceId,
            Status = QuestionnaireResponse.QuestionnaireResponseStatus.Completed,
            Authored = source.AuthoredAt.ToString("O"),
            Item = source.Items.Select(item => new QuestionnaireResponse.ItemComponent
            {
                LinkId = RequiredLinkId(item.LinkId),
                Text = item.Text,
                Answer = MapAnswers(item.Answer)
            }).ToList()
        };
        return result;
    }

    private static List<QuestionnaireResponse.AnswerComponent> MapAnswers(
        QuestionnaireResponseAnswerRepresentation source)
    {
        try
        {
            using var schema = JsonDocument.Parse(source.SourceAnswerSchemaJson);
            using var answer = JsonDocument.Parse(source.SourceAnswerJson);
            var answerType = schema.RootElement.GetProperty("answer").GetProperty("type")
                .GetString();
            var values = answerType switch
            {
                "FREE_TEXT" or "SINGLE_CHOICE" or "SYMPTOM_SELECTION" =>
                    [new FhirString(ReadText(answer.RootElement)) as DataType],
                "MULTIPLE_CHOICE" => ReadTexts(answer.RootElement)
                    .Select(value => new FhirString(value) as DataType)
                    .ToArray(),
                "INTEGER_SCALE" =>
                    [new Integer(ReadInteger(answer.RootElement))],
                "BOOLEAN" =>
                    [new FhirBoolean(ReadBoolean(answer.RootElement))],
                "DURATION" or "TEMPERATURE" =>
                    [ReadQuantity(answer.RootElement)],
                _ => throw InvalidAnswer("The frozen answer type is not supported by the R4 MVP.")
            };

            if (values.Length == 0)
            {
                throw InvalidAnswer("A source answer has no value to export.");
            }

            return values.Select(value => new QuestionnaireResponse.AnswerComponent
            {
                Value = value
            }).ToList();
        }
        catch (FhirR4BundleSerializationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or
            InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            throw InvalidAnswer(
                "A frozen answer cannot be truthfully translated to its declared R4 type.");
        }
    }

    private static Device MapDevice(DeviceRepresentation source, string resourceId) => new()
    {
        Id = resourceId,
        DeviceName =
        [
            new Device.DeviceNameComponent
            {
                Name = source.DeviceName.Name,
                Type = ParseDeviceNameType(source.DeviceName.Type)
            }
        ],
        Manufacturer = source.Manufacturer,
        ModelNumber = source.ModelNumber,
        Version = [new Device.VersionComponent { Value = source.Version.Value }],
        Type = new CodeableConcept { Text = source.TypeText }
    };

    private static Provenance MapProvenance(
        ProvenanceRepresentation source,
        string resourceId,
        string questionnaireResponseUrl,
        string deviceUrl) => new()
        {
            Id = resourceId,
            Target = [new ResourceReference(questionnaireResponseUrl)],
            Recorded = source.RecordedAt,
            Activity = new CodeableConcept(
                source.Activity.System,
                source.Activity.Code,
                source.Activity.Display),
            Agent =
            [
                new Provenance.AgentComponent
                {
                    Type = new CodeableConcept(
                        source.AgentType.System,
                        source.AgentType.Code,
                        source.AgentType.Display),
                    Who = new ResourceReference(deviceUrl)
                }
            ],
            Entity =
            [
                new Provenance.EntityComponent
                {
                    Role = Provenance.ProvenanceEntityRole.Source,
                    What = new ResourceReference(questionnaireResponseUrl)
                }
            ]
        };

    private static Bundle.EntryComponent Entry(string id, Resource resource) => new()
    {
        FullUrl = FullUrl(id),
        Resource = resource
    };

    private static string RequiredLinkId(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new FhirR4BundleSerializationException(
                "A stable questionnaire question code is required for item.linkId.")
            : value;

    private static string ReadText(JsonElement root)
    {
        var value = root.ValueKind == JsonValueKind.Object
            ? root.GetProperty("value")
            : root;
        return value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!
                : throw InvalidAnswer("A textual answer requires a non-empty string value.");
    }

    private static IReadOnlyList<string> ReadTexts(JsonElement root)
    {
        var values = root.ValueKind == JsonValueKind.Object
            ? root.GetProperty("values")
            : root;
        if (values.ValueKind != JsonValueKind.Array)
        {
            throw InvalidAnswer("A multiple-choice answer requires a string array.");
        }

        var result = values.EnumerateArray().Select(value =>
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!
                : throw InvalidAnswer(
                    "A multiple-choice answer contains a non-textual value.")).ToArray();
        return result;
    }

    private static int ReadInteger(JsonElement root)
    {
        var value = root.ValueKind == JsonValueKind.Object
            ? root.GetProperty("value")
            : root;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : throw InvalidAnswer("An integer-scale answer requires an integer value.");
    }

    private static bool ReadBoolean(JsonElement root)
    {
        var value = root.ValueKind == JsonValueKind.Object
            ? root.GetProperty("value")
            : root;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw InvalidAnswer("A boolean answer requires a boolean value.")
        };
    }

    private static Quantity ReadQuantity(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("value", out var value) ||
            !value.TryGetDecimal(out var numeric) ||
            !root.TryGetProperty("unit", out var unit) ||
            unit.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(unit.GetString()))
        {
            throw InvalidAnswer("A quantity answer requires numeric value and textual unit.");
        }

        return new Quantity { Value = numeric, Unit = unit.GetString() };
    }

    private static Hl7.Fhir.Model.DeviceNameType ParseDeviceNameType(string value) =>
        value switch
        {
            "manufacturer-name" => Hl7.Fhir.Model.DeviceNameType.ManufacturerName,
            _ => throw new FhirR4BundleSerializationException(
                "The established Device.deviceName.type is unsupported.")
        };

    private static string ResourceId(Guid exportId, string resourceType)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{IdentityNamespace}|{exportId:D}|{resourceType}"));
        var id = bytes[..16];
        id[6] = (byte)((id[6] & 0x0f) | 0x50);
        id[8] = (byte)((id[8] & 0x3f) | 0x80);
        return new Guid(id).ToString("D");
    }

    private static string FullUrl(string id) => $"urn:uuid:{id}";

    private static FhirR4BundleSerializationException InvalidAnswer(string message) =>
        new(message);
}
