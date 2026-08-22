using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;

namespace Beeexy.Infrastructure.Triage;

public sealed class ClinicalPathwayRegistry(IClinicalDefinitionProvider definitionProvider)
    : IClinicalPathwayRegistry
{
    private static readonly IReadOnlyDictionary<string, ClinicalPathwayCode> Recognized =
        ClinicalPathways.Recognized.ToDictionary(pathway => pathway.Value, StringComparer.Ordinal);

    public bool IsRecognized(ClinicalPathwayCode pathway)
    {
        ArgumentNullException.ThrowIfNull(pathway);
        return Recognized.ContainsKey(pathway.Value);
    }

    public bool IsSupported(ClinicalPathwayCode pathway)
    {
        ArgumentNullException.ThrowIfNull(pathway);
        return ClinicalPathways.Supported.Contains(pathway);
    }

    public async Task<ClinicalPathwayResolution> ResolveAsync(
        string pathwayCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pathwayCode) ||
            !Recognized.TryGetValue(pathwayCode.Trim(), out var pathway))
        {
            return new ClinicalPathwayResolution(
                ClinicalPathwayResolutionStatus.Unknown,
                null,
                null);
        }

        if (!IsSupported(pathway))
        {
            return new ClinicalPathwayResolution(
                ClinicalPathwayResolutionStatus.RecognizedButUnsupported,
                pathway,
                null);
        }

        return new ClinicalPathwayResolution(
            ClinicalPathwayResolutionStatus.Supported,
            pathway,
            await definitionProvider.GetActiveDefinitionAsync(pathway, cancellationToken));
    }
}
