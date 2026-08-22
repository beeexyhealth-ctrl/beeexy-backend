using Beeexy.Application.Triage;

namespace Beeexy.Infrastructure.Triage;

public sealed class UnavailableClinicalAiProvider : IClinicalAiProvider
{
    public Task<ClinicalAiProviderOutput> InterpretAsync(
        ClinicalAiInterpretationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        throw new ClinicalAiProviderException(
            ClinicalAiProviderFailureCategory.ConfigurationUnavailable);
    }
}
