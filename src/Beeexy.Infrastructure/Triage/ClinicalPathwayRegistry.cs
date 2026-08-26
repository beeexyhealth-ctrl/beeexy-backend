using Beeexy.Application.Triage;
using Beeexy.Domain.Triage;

namespace Beeexy.Infrastructure.Triage;

public sealed class ClinicalPathwayRegistry(IClinicalDefinitionProvider definitionProvider)
    : IClinicalPathwayRegistry
{
    private static readonly IReadOnlyDictionary<string, ClinicalPathwayCode> Recognized =
        ClinicalPathways.Recognized.ToDictionary(pathway => pathway.Value, StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, ClinicalPathwayCode> Aliases =
        new Dictionary<string, ClinicalPathwayCode>(StringComparer.Ordinal)
        {
            ["Headache"] = ClinicalPathways.Headache,
            ["Stomach pain"] = ClinicalPathways.AbdominalPain,
            ["Chest pain"] = ClinicalPathways.ChestPain,
            ["Fever"] = ClinicalPathways.Fever,
            ["Other"] = ClinicalPathways.OtherSymptoms
        };

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
        var normalizedInput = pathwayCode?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedInput) ||
            !Recognized.TryGetValue(normalizedInput, out var pathway) &&
            !Aliases.TryGetValue(normalizedInput, out pathway))
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
            await definitionProvider.GetActiveDefinitionAsync(
                pathway,
                ClinicalDefinitionPackageProfile.SimplifiedDemoIntake,
                cancellationToken));
    }
}
