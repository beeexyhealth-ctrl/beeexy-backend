using Beeexy.Application.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class InterpretPreTriageIntake(
    IClinicalPathwayRegistry pathwayRegistry,
    IClinicalAiProvider provider,
    IClinicalSafetyPolicy safetyPolicy,
    IClinicalAiOutputValidator outputValidator,
    IPreTriageInterpretationAuditLogger auditLogger)
{
    public const int MaximumTextLength = SubmitTriageAnswers.MaximumNaturalLanguageLength;

    public async Task<PreTriageIntakeInterpretationResult> ExecuteAsync(
        InterpretPreTriageIntakeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command);
        var text = command.Text!.Trim();

        var deterministic = await pathwayRegistry.ResolveAsync(text, cancellationToken);
        if (deterministic.Status == ClinicalPathwayResolutionStatus.Supported &&
            deterministic.Pathway is not null)
        {
            if (!IsUsableDemoPackage(deterministic.ActiveDefinition))
            {
                throw Unavailable(ClinicalAiProviderFailureCategory.ConfigurationUnavailable);
            }

            return Audited(
                new PreTriageIntakeInterpretationResult(
                    PreTriageIntakeResolution.Resolved,
                    deterministic.Pathway,
                    [],
                    []),
                usedAi: false);
        }

        var request = new ClinicalAiInterpretationRequest(text);
        var inputSafety = safetyPolicy.EvaluateInput(request);
        if (!inputSafety.AllowsProviderInterpretation)
        {
            return Audited(Unresolved(), usedAi: false);
        }

        var output = await InvokeProviderAsync(request, cancellationToken);
        var structuralIssue = ClinicalAiOutputValidator.ValidateStructure(output);
        if (structuralIssue.HasValue)
        {
            throw Unavailable(ClinicalAiProviderFailureCategory.InvalidStructuredResponse);
        }

        var outputSafety = safetyPolicy.EvaluateOutput(request, output);
        if (!outputSafety.AllowsProviderInterpretation &&
            outputSafety.Classification != ClinicalIntentClassification.Ambiguous)
        {
            return Audited(Unresolved(), usedAi: true);
        }

        var candidates = await ResolveCandidatePathwaysAsync(output, cancellationToken);
        var ambiguityKinds = output.Ambiguities!
            .Select(value => value.Kind)
            .ToHashSet();
        if (ambiguityKinds.Contains(ClinicalAiAmbiguityKind.Pathway) ||
            candidates.Count > 1)
        {
            return Audited(
                new PreTriageIntakeInterpretationResult(
                    PreTriageIntakeResolution.Ambiguous,
                    null,
                    OrderPathways(candidates),
                    []),
                usedAi: true);
        }

        if (ambiguityKinds.Contains(ClinicalAiAmbiguityKind.InsufficientContext) ||
            candidates.Count == 0 ||
            output.Intent == ClinicalIntentClassification.Ambiguous ||
            output.RequiresClarification)
        {
            return Audited(Unresolved(), usedAi: true);
        }

        var pathway = candidates.Single();
        var resolution = await pathwayRegistry.ResolveAsync(pathway.Value, cancellationToken);
        if (!IsUsableDemoPackage(resolution.ActiveDefinition))
        {
            throw Unavailable(ClinicalAiProviderFailureCategory.ConfigurationUnavailable);
        }

        var package = resolution.ActiveDefinition!;
        var demo = package.RuleDefinitions.DemoIntake!;
        var validationRequest = new ClinicalAiInterpretationRequest(
            text,
            pathway,
            allowedFactCodes: demo.ProgressionQuestionCodes,
            pinnedDefinition: package);
        var sanitizedOutput = output with
        {
            Intent = ClinicalIntentClassification.PreTriageInput,
            PathwayCandidate = pathway.Value,
            Symptoms = [],
            Ambiguities = [],
            RequiresClarification = false
        };
        var validation = await outputValidator.ValidateAsync(
            validationRequest,
            sanitizedOutput,
            cancellationToken);
        if (validation.PathwayStatus != ClinicalPathwayResolutionStatus.Supported ||
            validation.Pathway != pathway)
        {
            return Audited(Unresolved(), usedAi: true);
        }

        var progressionOrder = demo.ProgressionQuestionCodes
            .Select((code, index) => (code, index))
            .ToDictionary(value => value.code, value => value.index);
        var accepted = validation.Facts
            .Where(value =>
                value.Status == ClinicalAiCandidateStatus.AcceptedCandidate &&
                demo.ProgressionQuestionCodes.Contains(value.Code))
            .GroupBy(value => value.Code)
            .Select(value => value.First())
            .OrderBy(value => progressionOrder[value.Code])
            .Select(value => new AcceptedTriageAnswerValue(value.Code, value.Value))
            .ToArray();
        return Audited(
            new PreTriageIntakeInterpretationResult(
                PreTriageIntakeResolution.Resolved,
                pathway,
                [],
                accepted),
            usedAi: true);
    }

    private async Task<ClinicalAiProviderOutput> InvokeProviderAsync(
        ClinicalAiInterpretationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.InterpretAsync(request, cancellationToken) ??
                throw Unavailable(
                    ClinicalAiProviderFailureCategory.InvalidStructuredResponse);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(ClinicalAiProviderFailureCategory.Timeout);
        }
        catch (ClinicalAiProviderException exception)
        {
            throw Unavailable(exception.Category);
        }
        catch (PreTriageInterpretationUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw Unavailable(ClinicalAiProviderFailureCategory.Unavailable);
        }
    }

    private async Task<HashSet<ClinicalPathwayCode>> ResolveCandidatePathwaysAsync(
        ClinicalAiProviderOutput output,
        CancellationToken cancellationToken)
    {
        var candidates = new HashSet<ClinicalPathwayCode>();
        await AddSupportedAsync(output.PathwayCandidate, candidates, cancellationToken);
        foreach (var symptom in output.Symptoms!)
        {
            if (!string.IsNullOrWhiteSpace(symptom.Text) &&
                symptom.Confidence == ClinicalAiConfidenceSignal.Sufficient)
            {
                await AddSupportedAsync(
                    symptom.NormalizedPathwayCandidate,
                    candidates,
                    cancellationToken);
            }
        }

        return candidates;
    }

    private async Task AddSupportedAsync(
        string? candidate,
        ISet<ClinicalPathwayCode> candidates,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var resolution = await pathwayRegistry.ResolveAsync(candidate, cancellationToken);
        if (resolution.Status == ClinicalPathwayResolutionStatus.Supported &&
            resolution.Pathway is not null)
        {
            if (!IsUsableDemoPackage(resolution.ActiveDefinition))
            {
                throw Unavailable(ClinicalAiProviderFailureCategory.ConfigurationUnavailable);
            }

            candidates.Add(resolution.Pathway);
        }
    }

    private PreTriageIntakeInterpretationResult Audited(
        PreTriageIntakeInterpretationResult result,
        bool usedAi)
    {
        auditLogger.InterpretationEvaluated(
            result.Resolution,
            result.Pathway,
            usedAi,
            result.CandidateValues.Count);
        return result;
    }

    private PreTriageInterpretationUnavailableException Unavailable(
        ClinicalAiProviderFailureCategory failure)
    {
        auditLogger.InterpretationFailed(failure);
        return new PreTriageInterpretationUnavailableException(failure);
    }

    private static void ValidateCommand(InterpretPreTriageIntakeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Text) ||
            command.Text.Length > MaximumTextLength ||
            command.UnsupportedFields.Count > 0)
        {
            throw new RequestValidationException(
                "pre_triage.intake_interpretation_invalid",
                "A bounded first-message text value is required without unsupported fields.");
        }
    }

    private static bool IsUsableDemoPackage(ClinicalDefinitionPackage? package) =>
        package is
        {
            Profile: ClinicalDefinitionPackageProfile.SimplifiedDemoIntake,
            RuleDefinitions.DemoIntake: not null
        };

    private static IReadOnlyList<ClinicalPathwayCode> OrderPathways(
        IReadOnlySet<ClinicalPathwayCode> pathways) =>
        ClinicalPathways.Supported.Where(pathways.Contains).ToArray();

    private static PreTriageIntakeInterpretationResult Unresolved() => new(
        PreTriageIntakeResolution.Unresolved,
        null,
        [],
        []);
}

public sealed record InterpretPreTriageIntakeCommand(
    string? Text,
    IReadOnlyCollection<string> UnsupportedFields);

public enum PreTriageIntakeResolution
{
    Resolved,
    Ambiguous,
    Unresolved
}

public sealed record PreTriageIntakeInterpretationResult(
    PreTriageIntakeResolution Resolution,
    ClinicalPathwayCode? Pathway,
    IReadOnlyList<ClinicalPathwayCode> CandidatePathways,
    IReadOnlyList<AcceptedTriageAnswerValue> CandidateValues);

public sealed class PreTriageInterpretationUnavailableException(
    ClinicalAiProviderFailureCategory failure) : Exception(
        "Pre-triage interpretation is temporarily unavailable.")
{
    public ClinicalAiProviderFailureCategory Failure { get; } = failure;
}

public interface IPreTriageInterpretationAuditLogger
{
    void InterpretationEvaluated(
        PreTriageIntakeResolution resolution,
        ClinicalPathwayCode? pathway,
        bool usedAi,
        int acceptedCandidateCategoryCount);

    void InterpretationFailed(ClinicalAiProviderFailureCategory failure);
}
