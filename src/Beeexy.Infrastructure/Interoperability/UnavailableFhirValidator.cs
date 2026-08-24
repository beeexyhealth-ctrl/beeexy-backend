using Beeexy.Application.Interoperability;

namespace Beeexy.Infrastructure.Interoperability;

internal sealed class UnavailableFhirValidator : IFhirValidator
{
    public Task<FhirValidatorExecutionResult> ValidateAsync(
        FhirValidatorRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(FhirValidatorExecutionResult.UnsupportedSpecification());
    }
}
