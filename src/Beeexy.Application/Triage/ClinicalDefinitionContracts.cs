using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public interface IClinicalDefinitionProvider
{
    Task<ClinicalDefinitionPackage?> GetActiveDefinitionAsync(
        ClinicalPathwayCode pathway,
        CancellationToken cancellationToken = default);

    Task<ClinicalDefinitionPackage?> GetDefinitionAsync(
        ClinicalPathwayCode pathway,
        DefinitionVersion version,
        CancellationToken cancellationToken = default);
}

public enum ClinicalPathwayResolutionStatus
{
    Supported,
    RecognizedButUnsupported,
    Unknown
}

public sealed record ClinicalPathwayResolution(
    ClinicalPathwayResolutionStatus Status,
    ClinicalPathwayCode? Pathway,
    ClinicalDefinitionPackage? ActiveDefinition)
{
    public bool IsRecognized => Status != ClinicalPathwayResolutionStatus.Unknown;

    public bool IsSupported => Status == ClinicalPathwayResolutionStatus.Supported;
}

public interface IClinicalPathwayRegistry
{
    bool IsRecognized(ClinicalPathwayCode pathway);

    bool IsSupported(ClinicalPathwayCode pathway);

    Task<ClinicalPathwayResolution> ResolveAsync(
        string pathwayCode,
        CancellationToken cancellationToken = default);
}

public enum ClinicalDefinitionImportOutcome
{
    Imported,
    AlreadyImported
}

public sealed record ClinicalDefinitionImportResult(
    ClinicalDefinitionImportOutcome Outcome,
    ClinicalPathwayCode Pathway,
    DefinitionVersion Version);

public interface IClinicalDefinitionImporter
{
    Task<ClinicalDefinitionImportResult> ImportAsync(
        ClinicalDefinitionPackage package,
        CancellationToken cancellationToken = default);
}
