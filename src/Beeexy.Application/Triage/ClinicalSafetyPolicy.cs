using System.Text.RegularExpressions;

namespace Beeexy.Application.Triage;

public sealed partial class ClinicalSafetyPolicy : IClinicalSafetyPolicy
{
    private static readonly HashSet<string> OverrideTerms = new(StringComparer.Ordinal)
    {
        "bypass", "disregard", "ignore", "override"
    };

    private static readonly HashSet<string> InstructionTerms = new(StringComparer.Ordinal)
    {
        "instruction", "instructions", "previous", "prompt", "rules", "safety", "system"
    };

    private static readonly HashSet<string> MedicationTerms = new(StringComparer.Ordinal)
    {
        "antibiotic", "antibiotics", "dose", "dosage", "drug", "drugs", "medication",
        "medications", "medicine", "medicines", "pill", "pills"
    };

    private static readonly HashSet<string> RecommendationTerms = new(StringComparer.Ordinal)
    {
        "best", "give", "prescribe", "recommend", "should", "suggest", "tell"
    };

    private static readonly HashSet<string> AuthoritativeAdviceTerms = new(StringComparer.Ordinal)
    {
        "cure", "diagnose", "diagnosis", "treat", "treatment"
    };

    private static readonly HashSet<string> PersonalRequestTerms = new(StringComparer.Ordinal)
    {
        "give", "me", "my", "recommend", "should", "tell", "what"
    };

    private static readonly HashSet<string> NonClinicalTopicTerms = new(StringComparer.Ordinal)
    {
        "basketball", "football", "match", "soccer", "team"
    };

    public ClinicalSafetyDecision EvaluateInput(ClinicalAiInterpretationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var terms = Tokenize(request.UserMessage);
        if (terms.Count == 0)
        {
            return Block(ClinicalIntentClassification.Ambiguous, requiresClarification: true);
        }

        if (terms.Overlaps(OverrideTerms) && terms.Overlaps(InstructionTerms))
        {
            return Block(ClinicalIntentClassification.PotentialPromptInjection);
        }

        if (terms.Overlaps(MedicationTerms) && terms.Overlaps(RecommendationTerms))
        {
            return Block(ClinicalIntentClassification.PrescriptionRequest);
        }

        if (terms.Overlaps(AuthoritativeAdviceTerms) && terms.Overlaps(PersonalRequestTerms))
        {
            return Block(ClinicalIntentClassification.ProhibitedMedicalAdvice);
        }

        if (terms.Overlaps(NonClinicalTopicTerms))
        {
            return Block(ClinicalIntentClassification.OutOfScope);
        }

        return Allow(ClinicalIntentClassification.PreTriageInput);
    }

    public ClinicalSafetyDecision EvaluateOutput(
        ClinicalAiInterpretationRequest request,
        ClinicalAiProviderOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var inputDecision = EvaluateInput(request);
        if (!inputDecision.AllowsProviderInterpretation)
        {
            return inputDecision;
        }

        if (!Enum.IsDefined(output.Intent))
        {
            return Block(ClinicalIntentClassification.Ambiguous, requiresClarification: true);
        }

        return output.Intent switch
        {
            ClinicalIntentClassification.PreTriageInput =>
                Allow(ClinicalIntentClassification.PreTriageInput),
            ClinicalIntentClassification.Ambiguous =>
                Block(ClinicalIntentClassification.Ambiguous, requiresClarification: true),
            _ => Block(output.Intent)
        };
    }

    private static HashSet<string> Tokenize(string input)
    {
        return WordRegex().Matches(input.ToLowerInvariant())
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static ClinicalSafetyDecision Allow(ClinicalIntentClassification classification)
    {
        return new ClinicalSafetyDecision(classification, true, false);
    }

    private static ClinicalSafetyDecision Block(
        ClinicalIntentClassification classification,
        bool requiresClarification = false)
    {
        return new ClinicalSafetyDecision(classification, false, requiresClarification);
    }

    [GeneratedRegex("[\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
