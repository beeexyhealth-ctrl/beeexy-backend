using System.Text.RegularExpressions;
using Beeexy.Application.Common;

namespace Beeexy.Application.Ai;

public sealed partial class AiConversationRequestPolicy
{
    public string ValidatePurpose(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw Invalid("ai.conversation.purpose_invalid");
        }

        var normalized = purpose.Trim().Replace('-', '_').ToUpperInvariant();
        return normalized switch
        {
            AiConversationPurpose.GeneralHealth => normalized,
            AiConversationPurpose.MedicalTerms => normalized,
            AiConversationPurpose.SymptomDiscussion => normalized,
            AiConversationPurpose.ClinicianQuestions => normalized,
            _ => throw Invalid("ai.conversation.purpose_invalid")
        };
    }

    public string ValidateMessage(string? content)
    {
        if (string.IsNullOrWhiteSpace(content) ||
            content.Trim().Length > AiConversationOptions.MaximumMessageCharacters)
        {
            throw Invalid("ai.conversation.message_invalid");
        }

        var normalized = content.Trim();
        if (IllicitSubstanceRegex().IsMatch(normalized))
        {
            throw Invalid("ai.conversation.request_not_supported");
        }

        if (SeriousHarmRegex().IsMatch(normalized))
        {
            throw Invalid("ai.conversation.request_not_supported");
        }

        if (JailbreakRegex().IsMatch(normalized))
        {
            throw Invalid("ai.conversation.request_not_supported");
        }

        if (!HealthTopicRegex().IsMatch(normalized))
        {
            throw Invalid("ai.conversation.request_not_supported");
        }

        return normalized;
    }

    private static RequestValidationException Invalid(string code) => new(
        code,
        "The AI conversation request cannot be processed.");

    [GeneratedRegex(
        @"\b(?:how\s+(?:do\s+i|to)|instructions?\s+(?:for|to))[^.]{0,80}(?:make|manufacture|cook|synthesize|produce)[^.]{0,40}(?:meth(?:amphetamine)?|cocaine|heroin|fentanyl|illegal\s+drug|illicit\s+substance)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IllicitSubstanceRegex();

    [GeneratedRegex(
        @"\b(?:how\s+(?:do\s+i|to)|instructions?\s+(?:for|to)|best\s+way\s+to)[^.]{0,80}(?:kill|murder|poison|seriously\s+hurt|self[- ]harm|commit\s+suicide|hide\s+a\s+body)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeriousHarmRegex();

    [GeneratedRegex(
        @"\b(?:ignore|disregard|forget|override|bypass)\s+(?:all\s+)?(?:previous|prior|system|developer|safety|beeexy)[^.]{0,40}(?:instructions?|rules?|policy|prompt)|\b(?:reveal|show|print)\s+(?:your\s+)?(?:system|hidden|internal)\s+prompt\b|\bjailbreak\b|\bdeveloper\s+mode\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JailbreakRegex();

    [GeneratedRegex(
        @"\b(?:health|healthy|medical|medicine|medication|drug|doctor|physician|clinician|nurse|hospital|clinic|appointment|symptom|pain|ache|fever|cough|rash|nausea|vomit|dizz|fatigue|headache|migraine|blood|heart|lung|kidney|liver|stomach|skin|infection|disease|condition|diagnos|treatment|therapy|surgery|vaccine|vitamin|nutrition|diet|exercise|sleep|stress|anxiety|depression|pregnan|diabetes|cancer|hypertension|cholesterol|allerg|asthma|injury|wound|swelling|hydrate|hydration|term|questions?\s+(?:for|to\s+ask)|salud|m[eé]dic(?:o|a|os|as)?|medicamento|doctor|s[ií]ntoma|dolor|fiebre|tos|mareo|cansancio|enfermedad|nutrici[oó]n|embarazo|t[eé]rmino)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HealthTopicRegex();
}
