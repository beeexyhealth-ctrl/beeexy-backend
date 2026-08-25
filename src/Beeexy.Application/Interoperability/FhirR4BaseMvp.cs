namespace Beeexy.Application.Interoperability;

public static class FhirR4BaseMvp
{
    public const string FhirRelease = "4.0.1";
    public const string MappingVersion = "beeexy-fhir-r4-base-mvp-v1";
    public const string ArtifactKind = "fhir-r4-collection-bundle";
    public const string MediaType = FhirValidationSpecification.OfficialFhirJsonMediaType;

    public static FhirMappingSpecificationIdentity MappingSpecification() =>
        FhirMappingSpecificationIdentity.Create(
            MappingVersion,
            FhirRelease,
            FhirProfileResolution.NotApplicable());

    public static FhirValidationSpecification ValidationSpecification() =>
        FhirValidationSpecification.Create(
            FhirRelease,
            MappingVersion,
            FhirProfileResolution.NotApplicable());

    public static bool Matches(FhirMappingSpecificationIdentity value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Equals(value.FhirRelease, FhirRelease, StringComparison.Ordinal) &&
            string.Equals(value.MappingVersion, MappingVersion, StringComparison.Ordinal) &&
            value.ProfileResolution.Status == FhirProfileResolutionStatus.NotApplicable;
    }
}

public interface IFhirR4BundleSerializer
{
    byte[] Serialize(FhirSnapshot snapshot);
}

public sealed class FhirR4BundleSerializationException : Exception
{
    public FhirR4BundleSerializationException(string message)
        : base(message)
    {
    }
}
