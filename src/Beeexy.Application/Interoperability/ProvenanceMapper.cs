using Beeexy.Domain.Common;

namespace Beeexy.Application.Interoperability;

public sealed record ProvenanceActivityRepresentation(
    string System,
    string Code,
    string Display);

public sealed record ProvenanceAgentTypeRepresentation(
    string System,
    string Code,
    string Display);

public sealed class ProvenanceRepresentation
{
    internal ProvenanceRepresentation(
        ProvenanceMappingInput source,
        FhirMappingSpecificationIdentity mappingSpecification,
        IReadOnlyList<FhirUnresolvedMappingRequirement> unresolvedRequirements)
    {
        ExportId = source.GenerationTrace.ExportId;
        InternalProvenanceIdentity = source.GenerationTrace.Provenance;
        InternalTargetIdentity = source.Target;
        InternalAgentIdentity = source.Agent;
        InternalSourceEntityIdentity = source.SourceEntity;
        SourcePatientProfileId = source.PatientProfileId;
        SourceClinicalHistoryEventId = source.SourceClinicalHistoryEventId;
        SourceEpisodeId = source.SourceEpisodeId;
        SourceAssessmentId = source.SourceAssessmentId;
        RecordedAt = source.GenerationTrace.RecordedAt;
        MappingSpecification = mappingSpecification;
        UnresolvedRequirements = unresolvedRequirements;
    }

    public FhirConceptualResource Resource => FhirConceptualResource.Provenance;

    public EntityId ExportId { get; }

    public FhirLogicalResourceIdentity InternalProvenanceIdentity { get; }

    public FhirLogicalResourceIdentity InternalTargetIdentity { get; }

    public FhirLogicalResourceIdentity InternalAgentIdentity { get; }

    public FhirLogicalResourceIdentity InternalSourceEntityIdentity { get; }

    public EntityId SourcePatientProfileId { get; }

    public EntityId SourceClinicalHistoryEventId { get; }

    public EntityId SourceEpisodeId { get; }

    public EntityId SourceAssessmentId { get; }

    public DateTimeOffset RecordedAt { get; }

    public ProvenanceActivityRepresentation Activity => new(
        AndreaFhirMappingInventory.ProvenanceActivitySystem,
        AndreaFhirMappingInventory.ProvenanceActivityCode,
        AndreaFhirMappingInventory.ProvenanceActivityDisplay);

    public ProvenanceAgentTypeRepresentation AgentType => new(
        AndreaFhirMappingInventory.ProvenanceAgentTypeSystem,
        AndreaFhirMappingInventory.ProvenanceAgentTypeCode,
        AndreaFhirMappingInventory.ProvenanceAgentTypeDisplay);

    public string EntityRole => AndreaFhirMappingInventory.ProvenanceEntityRole;

    public string? TargetReference => null;

    public string? AgentReference => null;

    public string? SourceEntityReference => null;

    public FhirMappingSpecificationIdentity MappingSpecification { get; }

    public IReadOnlyList<FhirUnresolvedMappingRequirement> UnresolvedRequirements { get; }

    public bool CanSerializeAsFhir =>
        FhirR4BaseMvp.Matches(MappingSpecification) &&
        UnresolvedRequirements.Count == 0;
}

public sealed class ProvenanceMapper :
    IFhirMapper<ProvenanceMappingInput, ProvenanceRepresentation>
{
    private readonly FhirMappingSpecificationIdentity _mappingSpecification;

    public ProvenanceMapper(
        FhirMappingSpecificationIdentity mappingSpecification)
    {
        ArgumentNullException.ThrowIfNull(mappingSpecification);
        _mappingSpecification = mappingSpecification;
    }

    public ProvenanceRepresentation Map(ProvenanceMappingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (FhirR4BaseMvp.Matches(_mappingSpecification))
        {
            return new ProvenanceRepresentation(input, _mappingSpecification, []);
        }

        var unresolved = FhirRepresentationRequirements.From(_mappingSpecification);
        unresolved.Add(
            FhirUnresolvedMappingRequirement.ResourceIdentityAndReferenceStrategy);

        return new ProvenanceRepresentation(
            input,
            _mappingSpecification,
            unresolved.AsReadOnly());
    }
}
