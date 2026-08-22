using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class ClinicalAiOutputValidator(IClinicalPathwayRegistry pathwayRegistry)
    : IClinicalAiOutputValidator
{
    public async Task<ClinicalAiOutputValidationResult> ValidateAsync(
        ClinicalAiInterpretationRequest request,
        ClinicalAiProviderOutput output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);

        var structuralIssue = ValidateStructure(output);
        if (structuralIssue.HasValue)
        {
            return Rejected(structuralIssue.Value);
        }

        if (output.Intent != ClinicalIntentClassification.PreTriageInput)
        {
            return Rejected(ClinicalAiValidationIssue.InvalidIntent);
        }

        var pathwayCandidate = output.PathwayCandidate ?? request.SelectedPathway?.Value;
        if (string.IsNullOrWhiteSpace(pathwayCandidate))
        {
            return Rejected(ClinicalAiValidationIssue.MissingPathway);
        }

        var pathwayResolution = await pathwayRegistry.ResolveAsync(
            pathwayCandidate,
            cancellationToken);
        if (pathwayResolution.Status == ClinicalPathwayResolutionStatus.Unknown)
        {
            return Rejected(
                ClinicalAiValidationIssue.UnknownPathway,
                ClinicalPathwayResolutionStatus.Unknown);
        }

        if (pathwayResolution.Status ==
            ClinicalPathwayResolutionStatus.RecognizedButUnsupported)
        {
            return Unsupported(
                pathwayResolution,
                ClinicalAiValidationIssue.RecognizedButUnsupportedPathway);
        }

        var definition = request.PinnedDefinition ?? pathwayResolution.ActiveDefinition;
        if (definition is null)
        {
            return Unsupported(
                pathwayResolution,
                ClinicalAiValidationIssue.MissingActiveDefinition);
        }

        if (request.SelectedPathway is not null &&
            request.SelectedPathway != pathwayResolution.Pathway)
        {
            return Rejected(
                ClinicalAiValidationIssue.PathwayMismatch,
                pathwayResolution.Status,
                pathwayResolution.Pathway);
        }

        if (definition.Pathway != pathwayResolution.Pathway ||
            request.PinnedDefinition is not null &&
            request.PinnedDefinition.Pathway != request.SelectedPathway)
        {
            return Rejected(
                ClinicalAiValidationIssue.PathwayMismatch,
                pathwayResolution.Status,
                pathwayResolution.Pathway);
        }

        var issues = new List<ClinicalAiValidationIssue>();
        var facts = ValidateFacts(
            request,
            output.Facts!,
            definition,
            issues);
        var symptoms = await ValidateSymptomsAsync(
            output.Symptoms!,
            pathwayResolution.Pathway!,
            issues,
            cancellationToken);

        if (output.RequiresClarification || output.Ambiguities!.Count > 0)
        {
            issues.Add(ClinicalAiValidationIssue.AmbiguousOutput);
        }

        var outcome = DetermineOutcome(facts, symptoms, issues);
        return new ClinicalAiOutputValidationResult(
            outcome,
            pathwayResolution.Status,
            pathwayResolution.Pathway,
            facts,
            symptoms,
            issues.Distinct().ToArray());
    }

    private static ClinicalAiValidationIssue? ValidateStructure(ClinicalAiProviderOutput output)
    {
        if (!string.Equals(
                output.SchemaVersion,
                ClinicalAiProviderOutput.CurrentSchemaVersion,
                StringComparison.Ordinal) ||
            output.Facts is null ||
            output.Symptoms is null ||
            output.Ambiguities is null ||
            output.SchemaViolations is null ||
            !Enum.IsDefined(output.Intent) ||
            output.Facts.Any(value => value is null) ||
            output.Symptoms.Any(value => value is null) ||
            output.Ambiguities.Any(value => value is null) ||
            output.SchemaViolations.Any(value => !Enum.IsDefined(value)))
        {
            return ClinicalAiValidationIssue.MalformedProviderOutput;
        }

        if (output.SchemaViolations.Contains(
            ClinicalAiOutputViolation.ForbiddenClinicalAuthority))
        {
            return ClinicalAiValidationIssue.ForbiddenClinicalAuthority;
        }

        if (output.SchemaViolations.Count > 0)
        {
            return ClinicalAiValidationIssue.MalformedProviderOutput;
        }

        if (output.Ambiguities.Any(value => !Enum.IsDefined(value.Kind)))
        {
            return ClinicalAiValidationIssue.MalformedProviderOutput;
        }

        return null;
    }

    private static IReadOnlyList<ClinicalAiValidatedFactCandidate> ValidateFacts(
        ClinicalAiInterpretationRequest request,
        IReadOnlyList<ClinicalAiFactCandidate> candidates,
        ClinicalDefinitionPackage definition,
        ICollection<ClinicalAiValidationIssue> issues)
    {
        var questions = definition.Questions.ToDictionary(value => value.Code);
        var allowedCodes = request.AllowedFactCodes.ToHashSet();
        var knownFacts = request.KnownFacts
            .GroupBy(value => value.Code)
            .ToDictionary(value => value.Key, value => value.First().Value);
        var conflicts = candidates
            .GroupBy(value => value.Code)
            .Where(group => group.Select(value => ValueKey(value.Value)).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        var validated = new List<ClinicalAiValidatedFactCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            ClinicalAiValidationIssue? issue = null;
            var status = ClinicalAiCandidateStatus.AcceptedCandidate;
            var matchesKnown = false;

            if (!Enum.IsDefined(candidate.Confidence))
            {
                status = ClinicalAiCandidateStatus.Rejected;
                issue = ClinicalAiValidationIssue.MalformedProviderOutput;
            }
            else if (!questions.TryGetValue(candidate.Code, out var question))
            {
                status = ClinicalAiCandidateStatus.Rejected;
                issue = ClinicalAiValidationIssue.UnknownFactCode;
            }
            else if (allowedCodes.Count > 0 && !allowedCodes.Contains(candidate.Code))
            {
                status = ClinicalAiCandidateStatus.Unsupported;
                issue = ClinicalAiValidationIssue.FactOutsideAllowedVocabulary;
            }
            else if (conflicts.Contains(candidate.Code))
            {
                status = ClinicalAiCandidateStatus.NeedsClarification;
                issue = ClinicalAiValidationIssue.ConflictingFact;
            }
            else if (knownFacts.TryGetValue(candidate.Code, out var knownValue) &&
                !ValuesEqual(candidate.Value, knownValue))
            {
                status = ClinicalAiCandidateStatus.NeedsClarification;
                issue = ClinicalAiValidationIssue.ConflictingFact;
            }
            else if (candidate.Confidence != ClinicalAiConfidenceSignal.Sufficient)
            {
                status = ClinicalAiCandidateStatus.NeedsClarification;
                issue = ClinicalAiValidationIssue.InsufficientConfidence;
            }
            else
            {
                issue = ClinicalAnswerValueValidator.Validate(
                    candidate.Value,
                    question!,
                    definition);
                if (issue.HasValue)
                {
                    status = ClinicalAiCandidateStatus.Rejected;
                }
                else if (knownFacts.TryGetValue(candidate.Code, out knownValue))
                {
                    matchesKnown = ValuesEqual(candidate.Value, knownValue);
                }
            }

            if (issue.HasValue)
            {
                issues.Add(issue.Value);
            }

            validated.Add(new ClinicalAiValidatedFactCandidate(
                candidate.Code,
                candidate.Value,
                status,
                issue,
                matchesKnown));
        }

        return validated;
    }

    private async Task<IReadOnlyList<ClinicalAiValidatedSymptomCandidate>>
        ValidateSymptomsAsync(
            IReadOnlyList<ClinicalAiSymptomCandidate> candidates,
            ClinicalPathwayCode packagePathway,
            ICollection<ClinicalAiValidationIssue> issues,
            CancellationToken cancellationToken)
    {
        var validated = new List<ClinicalAiValidatedSymptomCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            ClinicalPathwayCode? pathway = null;
            ClinicalAiValidationIssue? issue = null;
            var status = ClinicalAiCandidateStatus.AcceptedCandidate;

            if (string.IsNullOrWhiteSpace(candidate.Text) ||
                !Enum.IsDefined(candidate.Confidence))
            {
                status = ClinicalAiCandidateStatus.Rejected;
                issue = ClinicalAiValidationIssue.InvalidSymptomCandidate;
            }
            else if (candidate.Confidence != ClinicalAiConfidenceSignal.Sufficient ||
                string.IsNullOrWhiteSpace(candidate.NormalizedPathwayCandidate))
            {
                status = ClinicalAiCandidateStatus.NeedsClarification;
                issue = ClinicalAiValidationIssue.InsufficientConfidence;
            }
            else
            {
                var resolution = await pathwayRegistry.ResolveAsync(
                    candidate.NormalizedPathwayCandidate,
                    cancellationToken);
                pathway = resolution.Pathway;
                if (resolution.Status == ClinicalPathwayResolutionStatus.Unknown)
                {
                    status = ClinicalAiCandidateStatus.Rejected;
                    issue = ClinicalAiValidationIssue.UnknownPathway;
                }
                else if (!resolution.IsSupported)
                {
                    status = ClinicalAiCandidateStatus.Unsupported;
                    issue = ClinicalAiValidationIssue.RecognizedButUnsupportedPathway;
                }
                else if (resolution.Pathway != packagePathway)
                {
                    status = ClinicalAiCandidateStatus.Rejected;
                    issue = ClinicalAiValidationIssue.PathwayMismatch;
                }
            }

            if (issue.HasValue)
            {
                issues.Add(issue.Value);
            }

            validated.Add(new ClinicalAiValidatedSymptomCandidate(
                candidate.Text,
                pathway,
                status,
                issue));
        }

        return validated;
    }

    private static ClinicalAiValidationOutcome DetermineOutcome(
        IReadOnlyList<ClinicalAiValidatedFactCandidate> facts,
        IReadOnlyList<ClinicalAiValidatedSymptomCandidate> symptoms,
        IReadOnlyCollection<ClinicalAiValidationIssue> issues)
    {
        if (facts.Any(value => value.Status == ClinicalAiCandidateStatus.Rejected) ||
            symptoms.Any(value => value.Status == ClinicalAiCandidateStatus.Rejected))
        {
            return ClinicalAiValidationOutcome.Rejected;
        }

        if (facts.Any(value => value.Status == ClinicalAiCandidateStatus.Unsupported) ||
            symptoms.Any(value => value.Status == ClinicalAiCandidateStatus.Unsupported))
        {
            return ClinicalAiValidationOutcome.Unsupported;
        }

        if (facts.Any(value => value.Status == ClinicalAiCandidateStatus.NeedsClarification) ||
            symptoms.Any(value => value.Status == ClinicalAiCandidateStatus.NeedsClarification) ||
            issues.Contains(ClinicalAiValidationIssue.AmbiguousOutput))
        {
            return ClinicalAiValidationOutcome.NeedsClarification;
        }

        return ClinicalAiValidationOutcome.Accepted;
    }

    private static bool ValuesEqual(
        ClinicalAiCandidateValue first,
        ClinicalAiCandidateValue second)
    {
        return ValueKey(first) == ValueKey(second);
    }

    private static string ValueKey(ClinicalAiCandidateValue value)
    {
        return value switch
        {
            ClinicalAiTextValue text => $"text:{text.Value}",
            ClinicalAiChoiceValue choice => $"choice:{choice.Value}",
            ClinicalAiMultipleChoiceValue multiple =>
                multiple.Values is null
                    ? "multiple:<null>"
                    : $"multiple:{string.Join('|', multiple.Values.Order(StringComparer.Ordinal))}",
            ClinicalAiIntegerValue integer => $"integer:{integer.Value}",
            ClinicalAiBooleanValue boolean => $"boolean:{boolean.Value}",
            ClinicalAiDurationValue duration => $"duration:{duration.Value}:{duration.Unit}",
            ClinicalAiTemperatureValue temperature =>
                $"temperature:{temperature.Value}:{temperature.Unit}",
            _ => "unknown"
        };
    }

    private static ClinicalAiOutputValidationResult Rejected(
        ClinicalAiValidationIssue issue,
        ClinicalPathwayResolutionStatus? pathwayStatus = null,
        ClinicalPathwayCode? pathway = null)
    {
        return new ClinicalAiOutputValidationResult(
            ClinicalAiValidationOutcome.Rejected,
            pathwayStatus,
            pathway,
            [],
            [],
            [issue]);
    }

    private static ClinicalAiOutputValidationResult Unsupported(
        ClinicalPathwayResolution resolution,
        ClinicalAiValidationIssue issue)
    {
        return new ClinicalAiOutputValidationResult(
            ClinicalAiValidationOutcome.Unsupported,
            resolution.Status,
            resolution.Pathway,
            [],
            [],
            [issue]);
    }
}
