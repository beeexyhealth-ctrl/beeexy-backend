using System.Globalization;
using System.Text.Json;

namespace Beeexy.Application.Interoperability;

public sealed class FhirSnapshotSerializer
{
    public byte[] Serialize(FhirSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("artifactKind", snapshot.ArtifactKind);
            writer.WriteString("formatVersion", snapshot.FormatVersion);
            writer.WriteString("mediaType", snapshot.MediaType);
            writer.WriteBoolean("officialFhirJson", snapshot.IsOfficialFhirJson);
            writer.WriteBoolean("completeFhirExport", snapshot.IsCompleteFhirExport);
            writer.WriteBoolean("canBeFhirValidated", snapshot.CanBeFhirValidated);
            writer.WriteString("completeness", "incomplete-required-resource-blocked");
            WriteGuid(writer, "exportId", snapshot.ExportId.Value);
            WriteInstant(writer, "generatedAt", snapshot.GeneratedAt);
            WriteMappingSpecification(writer, snapshot.MappingSpecification);
            WriteSource(writer, snapshot);
            WriteResourceOrder(writer, snapshot.ResourceOrder);
            WriteResources(writer, snapshot);
            WriteRiskAssessmentBlocker(writer, snapshot.RiskAssessmentBoundary);
            WriteRequirements(
                writer,
                "unresolvedRequirements",
                snapshot.UnresolvedRequirements);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteMappingSpecification(
        Utf8JsonWriter writer,
        FhirMappingSpecificationIdentity specification)
    {
        writer.WritePropertyName("mappingSpecification");
        writer.WriteStartObject();
        writer.WriteString("mappingVersion", specification.MappingVersion);
        if (specification.FhirRelease is null)
        {
            writer.WriteNull("fhirRelease");
        }
        else
        {
            writer.WriteString("fhirRelease", specification.FhirRelease);
        }

        writer.WriteString(
            "profileResolutionStatus",
            ProfileStatus(specification.ProfileResolution.Status));
        if (specification.ProfileResolution.Canonical is null)
        {
            writer.WriteNull("profileCanonical");
            writer.WriteNull("profileVersion");
        }
        else
        {
            writer.WriteString(
                "profileCanonical",
                specification.ProfileResolution.Canonical);
            writer.WriteString(
                "profileVersion",
                specification.ProfileResolution.Version);
        }

        writer.WriteEndObject();
    }

    private static void WriteSource(Utf8JsonWriter writer, FhirSnapshot snapshot)
    {
        var source = snapshot.QuestionnaireResponse;
        writer.WritePropertyName("source");
        writer.WriteStartObject();
        WriteGuid(writer, "patientProfileId", source.SourcePatientProfileId.Value);
        WriteGuid(
            writer,
            "clinicalHistoryEventId",
            source.SourceClinicalHistoryEventId.Value);
        WriteGuid(writer, "episodeId", source.SourceEpisodeId.Value);
        WriteGuid(
            writer,
            "assessmentId",
            snapshot.RiskAssessmentBoundary.SourceAssessmentId.Value);
        WriteGuid(
            writer,
            "clinicalRuleSetVersionId",
            snapshot.RiskAssessmentBoundary.SourceClinicalRuleSetVersionId.Value);
        WriteGuid(
            writer,
            "questionnaireVersionId",
            source.SourceQuestionnaireVersionId.Value);
        writer.WriteString("questionnaireCode", source.SourceQuestionnaireCode);
        writer.WriteString("questionnaireVersion", source.SourceQuestionnaireVersion);
        writer.WriteString(
            "questionnaireContentHash",
            source.SourceQuestionnaireContentHash);
        writer.WriteEndObject();
    }

    private static void WriteResourceOrder(
        Utf8JsonWriter writer,
        IReadOnlyList<FhirConceptualResource> order)
    {
        writer.WritePropertyName("resourceOrder");
        writer.WriteStartArray();
        foreach (var resource in order)
        {
            writer.WriteStringValue(ResourceName(resource));
        }

        writer.WriteEndArray();
    }

    private static void WriteResources(Utf8JsonWriter writer, FhirSnapshot snapshot)
    {
        writer.WritePropertyName("resources");
        writer.WriteStartArray();
        WriteQuestionnaireResponse(writer, snapshot.QuestionnaireResponse);
        WriteDevice(writer, snapshot.Device);
        WriteProvenance(writer, snapshot.Provenance);
        writer.WriteEndArray();
    }

    private static void WriteQuestionnaireResponse(
        Utf8JsonWriter writer,
        QuestionnaireResponseRepresentation representation)
    {
        writer.WriteStartObject();
        writer.WriteString("concept", "QuestionnaireResponse");
        writer.WriteString("statusConcept", representation.Status);
        WriteInstant(writer, "authoredAt", representation.AuthoredAt);
        writer.WritePropertyName("items");
        writer.WriteStartArray();
        foreach (var item in representation.Items)
        {
            writer.WriteStartObject();
            WriteGuid(writer, "sourceQuestionId", item.SourceQuestionId.Value);
            writer.WriteString("sourceQuestionCode", item.SourceQuestionCode);
            writer.WriteString("text", item.Text);
            writer.WriteNumber("displayOrder", item.DisplayOrder);
            writer.WriteNull("linkId");
            writer.WritePropertyName("answer");
            writer.WriteStartObject();
            WriteGuid(writer, "sourceAnswerId", item.Answer.SourceAnswerId.Value);
            writer.WriteString(
                "sourceAnswerSchemaJson",
                item.Answer.SourceAnswerSchemaJson);
            writer.WriteString("sourceAnswerJson", item.Answer.SourceAnswerJson);
            writer.WriteString("sourceKind", AnswerKind(item.Answer.SourceKind));
            WriteInstant(writer, "recordedAt", item.Answer.RecordedAt);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        WriteRequirements(
            writer,
            "unresolvedRequirements",
            representation.UnresolvedRequirements);
        writer.WriteEndObject();
    }

    private static void WriteDevice(
        Utf8JsonWriter writer,
        DeviceRepresentation representation)
    {
        writer.WriteStartObject();
        writer.WriteString("concept", "Device");
        writer.WriteString("softwareName", representation.DeviceName.Name);
        writer.WriteString("softwareNameTypeConcept", representation.DeviceName.Type);
        writer.WriteString("modelNumberConcept", representation.ModelNumber);
        writer.WriteString("runtimeVersion", representation.Version.Value);
        writer.WriteString("manufacturerConcept", representation.Manufacturer);
        writer.WriteString("typeTextConcept", representation.TypeText);
        WriteRequirements(
            writer,
            "unresolvedRequirements",
            representation.UnresolvedRequirements);
        writer.WriteEndObject();
    }

    private static void WriteProvenance(
        Utf8JsonWriter writer,
        ProvenanceRepresentation representation)
    {
        writer.WriteStartObject();
        writer.WriteString("concept", "Provenance");
        writer.WriteString(
            "internalProvenanceIdentity",
            representation.InternalProvenanceIdentity.LogicalId);
        writer.WriteString(
            "internalTargetIdentity",
            representation.InternalTargetIdentity.LogicalId);
        writer.WriteString(
            "internalAgentIdentity",
            representation.InternalAgentIdentity.LogicalId);
        writer.WriteString(
            "internalSourceEntityIdentity",
            representation.InternalSourceEntityIdentity.LogicalId);
        WriteInstant(writer, "recordedAt", representation.RecordedAt);
        writer.WritePropertyName("activityConcept");
        writer.WriteStartObject();
        writer.WriteString("system", representation.Activity.System);
        writer.WriteString("code", representation.Activity.Code);
        writer.WriteString("display", representation.Activity.Display);
        writer.WriteEndObject();
        writer.WritePropertyName("agentTypeConcept");
        writer.WriteStartObject();
        writer.WriteString("system", representation.AgentType.System);
        writer.WriteString("code", representation.AgentType.Code);
        writer.WriteString("display", representation.AgentType.Display);
        writer.WriteEndObject();
        writer.WriteString("entityRoleConcept", representation.EntityRole);
        writer.WriteNull("targetReference");
        writer.WriteNull("agentReference");
        writer.WriteNull("sourceEntityReference");
        WriteRequirements(
            writer,
            "unresolvedRequirements",
            representation.UnresolvedRequirements);
        writer.WriteEndObject();
    }

    private static void WriteRiskAssessmentBlocker(
        Utf8JsonWriter writer,
        RiskAssessmentGenerationBoundary boundary)
    {
        writer.WritePropertyName("blockedRequiredResources");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("concept", "RiskAssessment");
        writer.WriteString("status", "blocked-missing-authoritative-clinical-input");
        writer.WriteString("supportedStatusConcept", boundary.SupportedStatusConcept);
        writer.WriteString(
            "supportedDisclaimerConcept",
            boundary.SupportedDisclaimerConcept);
        WriteInstant(writer, "occurrenceAt", boundary.OccurrenceAt);
        WriteRequirements(
            writer,
            "unresolvedRequirements",
            boundary.UnresolvedRequirements);
        writer.WriteEndObject();
        writer.WriteEndArray();
    }

    private static void WriteRequirements(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<FhirUnresolvedMappingRequirement> requirements)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var requirement in requirements)
        {
            writer.WriteStringValue(RequirementName(requirement));
        }

        writer.WriteEndArray();
    }

    private static void WriteGuid(Utf8JsonWriter writer, string name, Guid value) =>
        writer.WriteString(name, value.ToString("D", CultureInfo.InvariantCulture));

    private static void WriteInstant(
        Utf8JsonWriter writer,
        string name,
        DateTimeOffset value) =>
        writer.WriteString(
            name,
            value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    private static string ResourceName(FhirConceptualResource resource) => resource switch
    {
        FhirConceptualResource.QuestionnaireResponse => "QuestionnaireResponse",
        FhirConceptualResource.RiskAssessment => "RiskAssessment",
        FhirConceptualResource.Device => "Device",
        FhirConceptualResource.Provenance => "Provenance",
        _ => throw new ArgumentOutOfRangeException(nameof(resource))
    };

    private static string AnswerKind(QuestionnaireResponseSourceAnswerKind kind) =>
        kind switch
        {
            QuestionnaireResponseSourceAnswerKind.Object => "object",
            QuestionnaireResponseSourceAnswerKind.Array => "array",
            QuestionnaireResponseSourceAnswerKind.String => "string",
            QuestionnaireResponseSourceAnswerKind.Number => "number",
            QuestionnaireResponseSourceAnswerKind.Boolean => "boolean",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static string ProfileStatus(FhirProfileResolutionStatus status) =>
        status switch
        {
            FhirProfileResolutionStatus.Unresolved => "unresolved",
            FhirProfileResolutionStatus.NotApplicable => "not-applicable",
            FhirProfileResolutionStatus.Specified => "specified",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    private static string RequirementName(
        FhirUnresolvedMappingRequirement requirement) => requirement switch
        {
            FhirUnresolvedMappingRequirement.FhirRelease => "fhir-release",
            FhirUnresolvedMappingRequirement.CanonicalProfilesAndVersions =>
                "canonical-profiles-and-versions",
            FhirUnresolvedMappingRequirement.QuestionnaireResponseResourceIdentity =>
                "questionnaire-response-resource-identity",
            FhirUnresolvedMappingRequirement.PatientResourceIdentity =>
                "patient-resource-identity",
            FhirUnresolvedMappingRequirement.QuestionnaireResourceIdentityAndVersionEncoding =>
                "questionnaire-resource-identity-and-version-encoding",
            FhirUnresolvedMappingRequirement.QuestionnaireItemLinkIdStrategy =>
                "questionnaire-item-link-id-strategy",
            FhirUnresolvedMappingRequirement.QuestionnaireAnswerTypeTranslation =>
                "questionnaire-answer-type-translation",
            FhirUnresolvedMappingRequirement.ResourceIdentityAndReferenceStrategy =>
                "resource-identity-and-reference-strategy",
            FhirUnresolvedMappingRequirement.RiskPredictionOutcome =>
                "risk-prediction-outcome",
            FhirUnresolvedMappingRequirement.RiskPredictionProbability =>
                "risk-prediction-probability",
            FhirUnresolvedMappingRequirement.RiskMitigation => "risk-mitigation",
            FhirUnresolvedMappingRequirement.SoftwareRuntimeVersion =>
                "software-runtime-version",
            _ => throw new ArgumentOutOfRangeException(nameof(requirement))
        };
}
