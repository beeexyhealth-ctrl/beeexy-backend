namespace Beeexy.Application.Triage;

public sealed class InterpretClinicalInput(
    IClinicalAiProvider provider,
    IClinicalSafetyPolicy safetyPolicy,
    IClinicalAiOutputValidator outputValidator)
{
    public async Task<ClinicalAiInterpretationResult> ExecuteAsync(
        ClinicalAiInterpretationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var inputSafety = safetyPolicy.EvaluateInput(request);
        if (!inputSafety.AllowsProviderInterpretation)
        {
            return Restricted(inputSafety);
        }

        ClinicalAiProviderOutput output;
        try
        {
            output = await provider.InterpretAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderFailure(ClinicalAiProviderFailureCategory.Timeout);
        }
        catch (ClinicalAiProviderException exception)
        {
            return ProviderFailure(exception.Category);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ProviderFailure(ClinicalAiProviderFailureCategory.Unavailable);
        }

        if (output is null)
        {
            return ProviderFailure(ClinicalAiProviderFailureCategory.InvalidStructuredResponse);
        }

        var outputSafety = safetyPolicy.EvaluateOutput(request, output);
        if (!outputSafety.AllowsProviderInterpretation)
        {
            return Restricted(outputSafety);
        }

        var validation = await outputValidator.ValidateAsync(
            request,
            output,
            cancellationToken);
        var outcome = validation.Outcome switch
        {
            ClinicalAiValidationOutcome.Accepted => ClinicalAiInterpretationOutcome.Accepted,
            ClinicalAiValidationOutcome.NeedsClarification =>
                ClinicalAiInterpretationOutcome.ClarificationRequired,
            ClinicalAiValidationOutcome.Unsupported => ClinicalAiInterpretationOutcome.Unsupported,
            _ => ClinicalAiInterpretationOutcome.InvalidProviderOutput
        };
        return new ClinicalAiInterpretationResult(
            outcome,
            outputSafety.Classification,
            validation);
    }

    private static ClinicalAiInterpretationResult Restricted(ClinicalSafetyDecision decision)
    {
        return new ClinicalAiInterpretationResult(
            decision.RequiresClarification
                ? ClinicalAiInterpretationOutcome.ClarificationRequired
                : ClinicalAiInterpretationOutcome.SafetyRestricted,
            decision.Classification,
            null);
    }

    private static ClinicalAiInterpretationResult ProviderFailure(
        ClinicalAiProviderFailureCategory failure)
    {
        var outcome = failure switch
        {
            ClinicalAiProviderFailureCategory.Timeout =>
                ClinicalAiInterpretationOutcome.ProviderTimeout,
            ClinicalAiProviderFailureCategory.InvalidStructuredResponse =>
                ClinicalAiInterpretationOutcome.InvalidProviderOutput,
            ClinicalAiProviderFailureCategory.RejectedOutput =>
                ClinicalAiInterpretationOutcome.ProviderRejected,
            ClinicalAiProviderFailureCategory.ConfigurationUnavailable =>
                ClinicalAiInterpretationOutcome.ConfigurationUnavailable,
            _ => ClinicalAiInterpretationOutcome.ProviderUnavailable
        };
        return new ClinicalAiInterpretationResult(
            outcome,
            ClinicalIntentClassification.PreTriageInput,
            null,
            failure);
    }
}
