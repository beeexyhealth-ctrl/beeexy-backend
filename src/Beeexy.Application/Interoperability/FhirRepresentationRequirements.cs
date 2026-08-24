namespace Beeexy.Application.Interoperability;

internal static class FhirRepresentationRequirements
{
    public static List<FhirUnresolvedMappingRequirement> From(
        FhirMappingSpecificationIdentity mappingSpecification)
    {
        ArgumentNullException.ThrowIfNull(mappingSpecification);
        var unresolved = new List<FhirUnresolvedMappingRequirement>();
        if (mappingSpecification.FhirRelease is null)
        {
            unresolved.Add(FhirUnresolvedMappingRequirement.FhirRelease);
        }

        if (mappingSpecification.ProfileResolution.Status ==
            FhirProfileResolutionStatus.Unresolved)
        {
            unresolved.Add(
                FhirUnresolvedMappingRequirement.CanonicalProfilesAndVersions);
        }

        return unresolved;
    }
}
