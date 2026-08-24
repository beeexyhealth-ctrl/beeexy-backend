namespace Beeexy.Application.Interoperability;

public enum FhirUnresolvedMappingRequirement
{
    FhirRelease,
    CanonicalProfilesAndVersions,
    QuestionnaireResponseResourceIdentity,
    PatientResourceIdentity,
    QuestionnaireResourceIdentityAndVersionEncoding,
    QuestionnaireItemLinkIdStrategy,
    QuestionnaireAnswerTypeTranslation,
    ResourceIdentityAndReferenceStrategy,
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

    public const string ProvenanceActivitySystem =
        "http://terminology.hl7.org/CodeSystem/v3-DataOperation";
    public const string ProvenanceActivityCode = "CREATE";
    public const string ProvenanceActivityDisplay = "create";
    public const string ProvenanceAgentTypeSystem =
        "http://terminology.hl7.org/CodeSystem/provenance-participant-type";
    public const string ProvenanceAgentTypeCode = "author";
    public const string ProvenanceAgentTypeDisplay = "Author";
    public const string ProvenanceEntityRole = "source";

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
                FhirUnresolvedMappingRequirement.QuestionnaireResponseResourceIdentity,
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
                FhirUnresolvedMappingRequirement.ResourceIdentityAndReferenceStrategy,
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
                FhirUnresolvedMappingRequirement.ResourceIdentityAndReferenceStrategy,
                FhirUnresolvedMappingRequirement.SoftwareRuntimeVersion
            ]),
        new(
            FhirConceptualResource.Provenance,
            typeof(ProvenanceMappingInput),
            [
                new("activity.coding.system", ProvenanceActivitySystem),
                new("activity.coding.code", ProvenanceActivityCode),
                new("activity.coding.display", ProvenanceActivityDisplay),
                new("agent.type.coding.system", ProvenanceAgentTypeSystem),
                new("agent.type.coding.code", ProvenanceAgentTypeCode),
                new("agent.type.coding.display", ProvenanceAgentTypeDisplay),
                new("entity.role", ProvenanceEntityRole)
            ],
            [
                FhirUnresolvedMappingRequirement.FhirRelease,
                FhirUnresolvedMappingRequirement.CanonicalProfilesAndVersions,
                FhirUnresolvedMappingRequirement.ResourceIdentityAndReferenceStrategy
            ])
    ];

    public static IReadOnlyList<string> SourceDocuments => SourceDocumentValues;

    public static IReadOnlyList<FhirMappingContractDescriptor> Contracts => ContractValues;
}
