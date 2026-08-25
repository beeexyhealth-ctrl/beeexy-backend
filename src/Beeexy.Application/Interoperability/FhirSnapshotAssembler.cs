namespace Beeexy.Application.Interoperability;

public sealed record FhirSnapshotAssemblyInput(
    QuestionnaireResponseMappingInput QuestionnaireResponse,
    RiskAssessmentMappingInput RiskAssessment,
    DeviceMappingInput Device,
    ProvenanceMappingInput Provenance);

public sealed class FhirSnapshotAssemblyException : Exception
{
    public FhirSnapshotAssemblyException(string message)
        : base(message)
    {
    }
}

public sealed class FhirSnapshotAssembler
{
    private static readonly IReadOnlyList<FhirConceptualResource> SupportedResourceOrder =
        Array.AsReadOnly(
        [
            FhirConceptualResource.QuestionnaireResponse,
            FhirConceptualResource.Device,
            FhirConceptualResource.Provenance
        ]);

    private readonly FhirMappingSpecificationIdentity _mappingSpecification;
    private readonly QuestionnaireResponseMapper _questionnaireResponseMapper;
    private readonly RiskAssessmentMapper _riskAssessmentMapper;
    private readonly DeviceMapper _deviceMapper;
    private readonly ProvenanceMapper _provenanceMapper;

    public FhirSnapshotAssembler(
        FhirMappingSpecificationIdentity mappingSpecification)
    {
        ArgumentNullException.ThrowIfNull(mappingSpecification);
        _mappingSpecification = mappingSpecification;
        _questionnaireResponseMapper = new QuestionnaireResponseMapper(
            mappingSpecification);
        _riskAssessmentMapper = new RiskAssessmentMapper(mappingSpecification);
        _deviceMapper = new DeviceMapper(mappingSpecification);
        _provenanceMapper = new ProvenanceMapper(mappingSpecification);
    }

    public FhirSnapshot Assemble(FhirSnapshotAssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var questionnaireResponse = _questionnaireResponseMapper.Map(
            input.QuestionnaireResponse);
        var riskAssessmentBoundary = _riskAssessmentMapper.Inspect(
            input.RiskAssessment);
        var device = _deviceMapper.Map(input.Device);
        var provenance = _provenanceMapper.Map(input.Provenance);

        EnsureConsistentSources(
            questionnaireResponse,
            riskAssessmentBoundary,
            provenance);

        var unresolved = questionnaireResponse.UnresolvedRequirements
            .Concat(FhirR4BaseMvp.Matches(_mappingSpecification)
                ? []
                : riskAssessmentBoundary.UnresolvedRequirements)
            .Concat(device.UnresolvedRequirements)
            .Concat(provenance.UnresolvedRequirements)
            .Distinct()
            .OrderBy(requirement => requirement)
            .ToArray();

        return new FhirSnapshot(
            provenance.ExportId,
            provenance.RecordedAt,
            _mappingSpecification,
            questionnaireResponse,
            device,
            provenance,
            riskAssessmentBoundary,
            SupportedResourceOrder,
            Array.AsReadOnly(unresolved));
    }

    private static void EnsureConsistentSources(
        QuestionnaireResponseRepresentation questionnaireResponse,
        RiskAssessmentGenerationBoundary riskAssessment,
        ProvenanceRepresentation provenance)
    {
        if (questionnaireResponse.SourcePatientProfileId !=
                riskAssessment.SourcePatientProfileId ||
            questionnaireResponse.SourcePatientProfileId !=
                provenance.SourcePatientProfileId ||
            questionnaireResponse.SourceClinicalHistoryEventId !=
                riskAssessment.SourceClinicalHistoryEventId ||
            questionnaireResponse.SourceClinicalHistoryEventId !=
                provenance.SourceClinicalHistoryEventId ||
            questionnaireResponse.SourceEpisodeId != riskAssessment.SourceEpisodeId ||
            questionnaireResponse.SourceEpisodeId != provenance.SourceEpisodeId ||
            riskAssessment.SourceAssessmentId != provenance.SourceAssessmentId ||
            provenance.InternalTargetIdentity.Resource !=
                (FhirR4BaseMvp.Matches(provenance.MappingSpecification)
                    ? FhirConceptualResource.QuestionnaireResponse
                    : FhirConceptualResource.RiskAssessment) ||
            provenance.InternalAgentIdentity.Resource != FhirConceptualResource.Device ||
            provenance.InternalSourceEntityIdentity.Resource !=
                FhirConceptualResource.QuestionnaireResponse)
        {
            throw new FhirSnapshotAssemblyException(
                "The snapshot representations do not share one authoritative source graph.");
        }
    }
}
