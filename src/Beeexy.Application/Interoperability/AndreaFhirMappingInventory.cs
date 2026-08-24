namespace Beeexy.Application.Interoperability;

public enum FhirUnresolvedMappingRequirement
{
    FhirRelease,
    CanonicalProfilesAndVersions,
    PatientResourceIdentity,
    QuestionnaireResourceIdentityAndVersionEncoding,
    QuestionnaireItemLinkIdStrategy,
    QuestionnaireAnswerTypeTranslation,
    RiskPredictionOutcome,
    RiskPredictionProbability,
    RiskMitigation,
    SoftwareRuntimeVersion
}

public sealed record FhirEstablishedMappingConstant(string Path, string Value);

public sealed record FhirMappingContractDescriptor(
    FhirConceptualResource Resource,
    Type InputType,
    IReadOnlyList<FhirEstablishedMappingConstant> EstablishedConstants,
    IReadOnlyList<FhirUnresolvedMappingRequirement> UnresolvedRequirements);

public static class AndreaFhirMappingInventory
{
    public const string CollectionDocument =
        "docs/fhir/beeexy-coleccion-recursos.md";
    public const string ProvenanceDeviceDocument =
        "docs/fhir/beeexy-provenance-device-ejemplo.md";
    public const string RiskAssessmentDocument =
        "docs/fhir/beeexy-riskassessment-ejemplo.md";

    public const string DeviceName = "Beeexy Triage Engine";
    public const string DeviceNameType = "manufacturer-name";
    public const string DeviceModelNumber = "triage-core";
    public const string DeviceManufacturer = "Beeexy Inc.";
    public const string DeviceTypeText = "Clinical decision support software";

    public const string RiskAssessmentDisclaimer =
        "Evaluación generada automáticamente por Beeexy a partir de las respuestas del paciente. No constituye un diagnóstico médico.";

    private static readonly IReadOnlyList<string> SourceDocumentValues =
    [
        CollectionDocument,
        ProvenanceDeviceDocument,
        RiskAssessmentDocument
    ];

    private static readonly IReadOnlyList<FhirMappingContractDescriptor> ContractValues =
    [
        new(
            FhirConceptualResource.QuestionnaireResponse,
            typeof(QuestionnaireResponseMappingInput),
            [
                new("status", "completed")
            ],
            [
                FhirUnresolvedMappingRequirement.FhirRelease,
                FhirUnresolvedMappingRequirement.CanonicalProfilesAndVersions,
                FhirUnresolvedMappingRequirement.PatientResourceIdentity,
                FhirUnresolvedMappingRequirement.QuestionnaireResourceIdentityAndVersionEncoding,
                FhirUnresolvedMappingRequirement.QuestionnaireItemLinkIdStrategy,
                FhirUnresolvedMappingRequirement.QuestionnaireAnswerTypeTranslation
            ]),
        new(
            FhirConceptualResource.RiskAssessment,
            typeof(RiskAssessmentMappingInput),
            [
                new("status", "final"),
                new("basis", "QuestionnaireResponse reference is required by Beeexy"),
                new("note.text", RiskAssessmentDisclaimer)
            ],
            [
                FhirUnresolvedMappingRequirement.FhirRelease,
                FhirUnresolvedMappingRequirement.CanonicalProfilesAndVersions,
                FhirUnresolvedMappingRequirement.PatientResourceIdentity,
                FhirUnresolvedMappingRequirement.RiskPredictionOutcome,
                FhirUnresolvedMappingRequirement.RiskPredictionProbability,
                FhirUnresolvedMappingRequirement.RiskMitigation
            ]),
        new(
            FhirConceptualResource.Device,
            typeof(DeviceMappingInput),
            [
                new("deviceName.name", DeviceName),
                new("deviceName.type", DeviceNameType),
                new("modelNumber", DeviceModelNumber),
                new("manufacturer", DeviceManufacturer),
                new("type.text", DeviceTypeText)
            ],
            [
                FhirUnresolvedMappingRequirement.FhirRelease,
                FhirUnresolvedMappingRequirement.CanonicalProfilesAndVersions,
                FhirUnresolvedMappingRequirement.SoftwareRuntimeVersion
            ]),
        new(
            FhirConceptualResource.Provenance,
            typeof(ProvenanceMappingInput),
            [
                new("activity.coding.system", "http://terminology.hl7.org/CodeSystem/v3-DataOperation"),
                new("activity.coding.code", "CREATE"),
                new("activity.coding.display", "create"),
                new("agent.type.coding.system", "http://terminology.hl7.org/CodeSystem/provenance-participant-type"),
                new("agent.type.coding.code", "author"),
                new("agent.type.coding.display", "Author"),
                new("entity.role", "source")
            ],
            [
                FhirUnresolvedMappingRequirement.FhirRelease,
                FhirUnresolvedMappingRequirement.CanonicalProfilesAndVersions
            ])
    ];

    public static IReadOnlyList<string> SourceDocuments => SourceDocumentValues;

    public static IReadOnlyList<FhirMappingContractDescriptor> Contracts => ContractValues;
}
