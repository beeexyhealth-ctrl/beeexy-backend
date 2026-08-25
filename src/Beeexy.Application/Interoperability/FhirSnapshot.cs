using Beeexy.Domain.Common;

namespace Beeexy.Application.Interoperability;

public static class FhirSnapshotArtifactFormat
{
    public const string ArtifactKind =
        "beeexy-release-neutral-interoperability-snapshot";
    public const string FormatVersion = "1";
    public const string MediaType =
        "application/vnd.beeexy.interoperability-snapshot+json";
    public const string UnresolvedFhirReleaseMarker =
        "UNRESOLVED_RELEASE_NEUTRAL_SNAPSHOT";
}

public enum FhirSnapshotCompleteness
{
    IncompleteRequiredResourceBlocked = 1,
    CompleteR4MvpRiskAssessmentDeferred = 2
}

public sealed class FhirSnapshot
{
    internal FhirSnapshot(
        EntityId exportId,
        DateTimeOffset generatedAt,
        FhirMappingSpecificationIdentity mappingSpecification,
        QuestionnaireResponseRepresentation questionnaireResponse,
        DeviceRepresentation device,
        ProvenanceRepresentation provenance,
        RiskAssessmentGenerationBoundary riskAssessmentBoundary,
        IReadOnlyList<FhirConceptualResource> resourceOrder,
        IReadOnlyList<FhirUnresolvedMappingRequirement> unresolvedRequirements)
    {
        ExportId = exportId;
        GeneratedAt = generatedAt;
        MappingSpecification = mappingSpecification;
        QuestionnaireResponse = questionnaireResponse;
        Device = device;
        Provenance = provenance;
        RiskAssessmentBoundary = riskAssessmentBoundary;
        ResourceOrder = resourceOrder;
        UnresolvedRequirements = unresolvedRequirements;
    }

    public EntityId ExportId { get; }

    public string ArtifactKind => FhirSnapshotArtifactFormat.ArtifactKind;

    public string FormatVersion => FhirSnapshotArtifactFormat.FormatVersion;

    public string MediaType => FhirSnapshotArtifactFormat.MediaType;

    public DateTimeOffset GeneratedAt { get; }

    public FhirMappingSpecificationIdentity MappingSpecification { get; }

    public QuestionnaireResponseRepresentation QuestionnaireResponse { get; }

    public DeviceRepresentation Device { get; }

    public ProvenanceRepresentation Provenance { get; }

    public RiskAssessmentGenerationBoundary RiskAssessmentBoundary { get; }

    public IReadOnlyList<FhirConceptualResource> ResourceOrder { get; }

    public IReadOnlyList<FhirUnresolvedMappingRequirement> UnresolvedRequirements { get; }

    public FhirSnapshotCompleteness Completeness =>
        FhirR4BaseMvp.Matches(MappingSpecification) &&
        UnresolvedRequirements.Count == 0
            ? FhirSnapshotCompleteness.CompleteR4MvpRiskAssessmentDeferred
            : FhirSnapshotCompleteness.IncompleteRequiredResourceBlocked;

    public bool IsOfficialFhirJson => false;

    public bool IsCompleteFhirExport =>
        Completeness == FhirSnapshotCompleteness.CompleteR4MvpRiskAssessmentDeferred;

    public bool CanBeFhirValidated => IsCompleteFhirExport;
}
