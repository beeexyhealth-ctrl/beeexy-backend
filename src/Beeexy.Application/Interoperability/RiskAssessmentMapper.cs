using Beeexy.Domain.Common;

namespace Beeexy.Application.Interoperability;

public sealed class RiskAssessmentGenerationBoundary
{
    internal RiskAssessmentGenerationBoundary(
        RiskAssessmentMappingInput source,
        FhirMappingSpecificationIdentity mappingSpecification,
        IReadOnlyList<FhirUnresolvedMappingRequirement> unresolvedRequirements)
    {
        SourcePatientProfileId = source.PatientProfileId;
        SourceClinicalHistoryEventId = source.SourceClinicalHistoryEventId;
        SourceEpisodeId = source.EpisodeId;
        SourceAssessmentId = source.AssessmentId;
        SourceClinicalRuleSetVersionId = source.ClinicalRuleSetVersionId;
        OccurrenceAt = source.OccurrenceAt;
        MappingSpecification = mappingSpecification;
        UnresolvedRequirements = unresolvedRequirements;
    }

    public FhirConceptualResource Resource => FhirConceptualResource.RiskAssessment;

    public string SupportedStatusConcept => "final";

    public string SupportedDisclaimerConcept =>
        AndreaFhirMappingInventory.RiskAssessmentDisclaimer;

    public EntityId SourcePatientProfileId { get; }

    public EntityId SourceClinicalHistoryEventId { get; }

    public EntityId SourceEpisodeId { get; }

    public EntityId SourceAssessmentId { get; }

    public EntityId SourceClinicalRuleSetVersionId { get; }

    public DateTimeOffset OccurrenceAt { get; }

    public string? LogicalId => null;

    public string? SubjectReference => null;

    public string? BasisReference => null;

    public FhirMappingSpecificationIdentity MappingSpecification { get; }

    public IReadOnlyList<FhirUnresolvedMappingRequirement> UnresolvedRequirements { get; }

    public bool IsConcreteGenerationBlocked => true;

    public bool CanSerializeAsFhir => false;
}

public sealed class RiskAssessmentGenerationBlockedException : Exception
{
    public RiskAssessmentGenerationBlockedException(
        RiskAssessmentGenerationBoundary boundary)
        : base(
            "RiskAssessment generation is blocked because the authoritative " +
            "ClinicalAssessment has no prediction outcome, probability, or mitigation.")
    {
        ArgumentNullException.ThrowIfNull(boundary);
        Boundary = boundary;
    }

    public RiskAssessmentGenerationBoundary Boundary { get; }
}

public sealed class RiskAssessmentMapper :
    IFhirMapper<RiskAssessmentMappingInput, RiskAssessmentGenerationBoundary>
{
    private readonly FhirMappingSpecificationIdentity _mappingSpecification;

    public RiskAssessmentMapper(
        FhirMappingSpecificationIdentity mappingSpecification)
    {
        ArgumentNullException.ThrowIfNull(mappingSpecification);
        _mappingSpecification = mappingSpecification;
    }

    public RiskAssessmentGenerationBoundary Inspect(
        RiskAssessmentMappingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var unresolved = FhirRepresentationRequirements.From(_mappingSpecification);
        unresolved.Add(FhirUnresolvedMappingRequirement.PatientResourceIdentity);
        unresolved.Add(
            FhirUnresolvedMappingRequirement.ResourceIdentityAndReferenceStrategy);
        unresolved.AddRange(input.UnresolvedRequirements);

        return new RiskAssessmentGenerationBoundary(
            input,
            _mappingSpecification,
            unresolved.AsReadOnly());
    }

    public RiskAssessmentGenerationBoundary Map(RiskAssessmentMappingInput input)
    {
        throw new RiskAssessmentGenerationBlockedException(Inspect(input));
    }
}
