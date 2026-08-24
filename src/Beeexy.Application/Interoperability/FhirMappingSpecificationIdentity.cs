using Beeexy.Domain.Interoperability;

namespace Beeexy.Application.Interoperability;

public enum FhirProfileResolutionStatus
{
    Unresolved = 1,
    NotApplicable = 2,
    Specified = 3
}

public sealed record FhirProfileResolution
{
    private FhirProfileResolution(
        FhirProfileResolutionStatus status,
        string? canonical,
        string? version)
    {
        Status = status;
        Canonical = canonical;
        Version = version;
    }

    public FhirProfileResolutionStatus Status { get; }

    public string? Canonical { get; }

    public string? Version { get; }

    public static FhirProfileResolution Unresolved() =>
        new(FhirProfileResolutionStatus.Unresolved, null, null);

    public static FhirProfileResolution NotApplicable() =>
        new(FhirProfileResolutionStatus.NotApplicable, null, null);

    public static FhirProfileResolution Specified(string canonical, string version)
    {
        return new FhirProfileResolution(
            FhirProfileResolutionStatus.Specified,
            MappingText.Required(
                canonical,
                FhirExportVersionMetadata.MaximumProfileCanonicalLength,
                nameof(canonical)),
            MappingText.Required(
                version,
                FhirExportVersionMetadata.MaximumVersionLength,
                nameof(version)));
    }
}

public sealed record FhirMappingSpecificationIdentity
{
    private FhirMappingSpecificationIdentity(
        string mappingVersion,
        string? fhirRelease,
        FhirProfileResolution profileResolution)
    {
        MappingVersion = mappingVersion;
        FhirRelease = fhirRelease;
        ProfileResolution = profileResolution;
    }

    public string MappingVersion { get; }

    public string? FhirRelease { get; }

    public FhirProfileResolution ProfileResolution { get; }

    public bool IsReadyForExport =>
        FhirRelease is not null &&
        ProfileResolution.Status != FhirProfileResolutionStatus.Unresolved;

    public static FhirMappingSpecificationIdentity Create(
        string mappingVersion,
        string? fhirRelease = null,
        FhirProfileResolution? profileResolution = null)
    {
        return new FhirMappingSpecificationIdentity(
            MappingText.Required(
                mappingVersion,
                FhirExportVersionMetadata.MaximumVersionLength,
                nameof(mappingVersion)),
            fhirRelease is null
                ? null
                : MappingText.Required(
                    fhirRelease,
                    FhirExportVersionMetadata.MaximumVersionLength,
                    nameof(fhirRelease)),
            profileResolution ?? FhirProfileResolution.Unresolved());
    }

    public FhirExportVersionMetadata ToExportVersionMetadata()
    {
        if (!IsReadyForExport)
        {
            throw new InvalidOperationException(
                "FHIR release and profile applicability must be explicitly resolved before export generation.");
        }

        return ProfileResolution.Status switch
        {
            FhirProfileResolutionStatus.NotApplicable =>
                FhirExportVersionMetadata.Create(FhirRelease!, MappingVersion),
            FhirProfileResolutionStatus.Specified =>
                FhirExportVersionMetadata.Create(
                    FhirRelease!,
                    MappingVersion,
                    ProfileResolution.Canonical,
                    ProfileResolution.Version),
            _ => throw new InvalidOperationException(
                "FHIR profile applicability is unresolved.")
        };
    }
}
