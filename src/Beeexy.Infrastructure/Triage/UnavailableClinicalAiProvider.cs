using Beeexy.Application.Ai;
using Beeexy.Application.Triage;

namespace Beeexy.Infrastructure.Triage;

public sealed class UnavailableClinicalAiProvider : IClinicalAiProvider, IAiProvider
{
    public string ProviderIdentifier => "unconfigured";

    public string ModelIdentifier => "unconfigured";

    public Task<ClinicalAiProviderOutput> InterpretAsync(
        ClinicalAiInterpretationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        throw new ClinicalAiProviderException(
            ClinicalAiProviderFailureCategory.ConfigurationUnavailable);
    }

    public Task<AiProviderResponse> ExecuteAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        throw new AiProviderException(AiProviderFailureCategory.ConfigurationUnavailable);
    }
}
