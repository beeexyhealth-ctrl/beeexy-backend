using System.Text.Json;
using System.Text.RegularExpressions;
using Beeexy.Domain.Ai;

namespace Beeexy.Application.Ai;

public sealed partial class BeeexyAiSafetyValidator(AiSafetyProductContent content)
    : IAiSafetyValidator
{
    public AiSafetyDecision Validate(AiSafetyValidationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        AiContractGuard.Identifier(input.WorkloadIdentifier, nameof(input.WorkloadIdentifier));
        if (string.IsNullOrWhiteSpace(input.StructurallyValidatedContent))
        {
            return Rejected(
                AiSafetyCategory.Malformed,
                AiSafetyReasonCode.MalformedOutput);
        }

        try
        {
            using var document = JsonDocument.Parse(input.StructurallyValidatedContent);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Rejected(
                    AiSafetyCategory.Malformed,
                    AiSafetyReasonCode.MalformedOutput);
            }

            if (IsExplicitlyUnsupported(document.RootElement))
            {
                return Rejected(
                    AiSafetyCategory.Unsupported,
                    AiSafetyReasonCode.UnsupportedOutput);
            }

            if (ContainsNamedValue(
                    document.RootElement,
                    "probability",
                    "diseaseprobability",
                    "likelihood",
                    "diseaselikelihood"))
            {
                return Rejected(
                    AiSafetyCategory.Diagnosis,
                    AiSafetyReasonCode.DiseaseProbability);
            }

            if (ContainsNamedValue(
                    document.RootElement,
                    "diagnosis",
                    "confirmeddiagnosis"))
            {
                return Rejected(
                    AiSafetyCategory.Diagnosis,
                    AiSafetyReasonCode.DefinitiveDiagnosis);
            }

            if (ContainsNamedValue(
                    document.RootElement,
                    "urgency",
                    "urgencyclassification",
                    "triageurgency"))
            {
                return Rejected(
                    AiSafetyCategory.UnsafeMedicalAdvice,
                    AiSafetyReasonCode.AuthoritativeUrgency,
                    useCriticalFallback: true);
            }

            var text = ExtractText(document.RootElement);
            if (string.IsNullOrWhiteSpace(text))
            {
                return Rejected(
                    AiSafetyCategory.Unsupported,
                    AiSafetyReasonCode.UnsupportedOutput);
            }

            if (DiseaseProbabilityRegex().IsMatch(text))
            {
                return Rejected(
                    AiSafetyCategory.Diagnosis,
                    AiSafetyReasonCode.DiseaseProbability);
            }

            var medicationReason = MedicationReason(text);
            if (medicationReason.HasValue)
            {
                return Rejected(AiSafetyCategory.Prescription, medicationReason.Value);
            }

            if (DefinitiveDiagnosisRegex().IsMatch(text))
            {
                return Rejected(
                    AiSafetyCategory.Diagnosis,
                    AiSafetyReasonCode.DefinitiveDiagnosis);
            }

            if (EmergencyInstructionRegex().IsMatch(text))
            {
                return Rejected(
                    AiSafetyCategory.UnsafeMedicalAdvice,
                    AiSafetyReasonCode.EmergencyInstruction,
                    useCriticalFallback: true);
            }

            if (AuthoritativeUrgencyRegex().IsMatch(text))
            {
                return Rejected(
                    AiSafetyCategory.UnsafeMedicalAdvice,
                    AiSafetyReasonCode.AuthoritativeUrgency,
                    useCriticalFallback: true);
            }

            if (UnsafeCareInstructionRegex().IsMatch(text))
            {
                return Rejected(
                    AiSafetyCategory.UnsafeMedicalAdvice,
                    AiSafetyReasonCode.UnsafeCareInstruction);
            }

            return AiSafetyDecision.Approved(content.PolicyVersion);
        }
        catch (JsonException)
        {
            return Rejected(
                AiSafetyCategory.Malformed,
                AiSafetyReasonCode.MalformedOutput);
        }
    }

    private AiSafetyDecision Rejected(
        AiSafetyCategory category,
        AiSafetyReasonCode reason,
        bool useCriticalFallback = false) =>
        AiSafetyDecision.Rejected(
            category,
            reason,
            content.PolicyVersion,
            useCriticalFallback);

    private static AiSafetyReasonCode? MedicationReason(string text)
    {
        if (PrescriptionRegex().IsMatch(text))
        {
            return AiSafetyReasonCode.PrescriptionInstruction;
        }

        if (MedicationStartRegex().IsMatch(text))
        {
            return AiSafetyReasonCode.MedicationStart;
        }

        if (MedicationStopRegex().IsMatch(text))
        {
            return AiSafetyReasonCode.MedicationStop;
        }

        if (DosageChangeRegex().IsMatch(text))
        {
            return AiSafetyReasonCode.DosageChange;
        }

        return MedicationChangeRegex().IsMatch(text)
            ? AiSafetyReasonCode.MedicationChange
            : null;
    }

    private static bool IsExplicitlyUnsupported(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(IsExplicitlyUnsupported);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("supported") &&
                property.Value.ValueKind == JsonValueKind.False)
            {
                return true;
            }

            if ((property.NameEquals("status") || property.NameEquals("outcome")) &&
                property.Value.ValueKind == JsonValueKind.String &&
                string.Equals(
                    property.Value.GetString(),
                    "unsupported",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array &&
                IsExplicitlyUnsupported(property.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsNamedValue(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(item => ContainsNamedValue(item, names));
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            var normalizedName = NormalizePropertyName(property.Name);
            if (names.Contains(normalizedName, StringComparer.Ordinal) &&
                HasMeaningfulValue(property.Value))
            {
                return true;
            }

            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array &&
                ContainsNamedValue(property.Value, names))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMeaningfulValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() > 0,
        _ => true
    };

    private static string NormalizePropertyName(string value) =>
        new(value.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static string ExtractText(JsonElement root)
    {
        var values = new List<string>();
        CollectText(root, values);
        return string.Join(' ', values).ToLowerInvariant();
    }

    private static void CollectText(JsonElement element, ICollection<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectText(property.Value, values);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectText(item, values);
                }

                break;
        }
    }

    [GeneratedRegex(
        @"\b(?:\d{1,3}(?:\.\d+)?\s*%\s*(?:chance|probability|likely)|(?:chance|probability|likelihood)\s+(?:that\s+)?(?:you\s+)?(?:have|of having)?[^.]{0,40}\d{1,3}(?:\.\d+)?\s*%|\d{1,3}(?:\.\d+)?\s*%\s+likely\s+to\s+have)(?!\w)",
        RegexOptions.CultureInvariant)]
    private static partial Regex DiseaseProbabilityRegex();

    [GeneratedRegex(
        @"\b(?:you\s+(?:definitely\s+|certainly\s+)?have|your\s+diagnosis\s+is|this\s+confirms|this\s+proves|i\s+diagnose\s+you\s+with|tienes\s+definitivamente|tu\s+diagn[oó]stico\s+es|esto\s+confirma)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex DefinitiveDiagnosisRegex();

    [GeneratedRegex(
        @"\b(?:i\s+prescribe|prescription\s+is|take\s+\d+(?:\.\d+)?\s*(?:mg|mcg|g|ml)|prescribo|receta(?:r)?\s+)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex PrescriptionRegex();

    [GeneratedRegex(
        @"\b(?:start|begin|commence|inicia|empieza)\s+(?:taking|using|tomando|a\s+tomar)?\s*(?:the\s+|your\s+|el\s+|la\s+)?(?:medication|medicine|drug|antibiotic|ibuprofen|aspirin|acetaminophen|paracetamol|metformin|amoxicillin|insulin|medicamento|medicina|antibi[oó]tico)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex MedicationStartRegex();

    [GeneratedRegex(
        @"\b(?:stop|discontinue|cease|suspend|deja|det[eé]n)\s+(?:taking|using|tomando|de\s+tomar)?\s*(?:the\s+|your\s+|el\s+|la\s+)?(?:medication|medicine|drug|antibiotic|ibuprofen|aspirin|acetaminophen|paracetamol|metformin|amoxicillin|insulin|medicamento|medicina|antibi[oó]tico)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex MedicationStopRegex();

    [GeneratedRegex(
        @"\b(?:increase|decrease|double|halve|raise|lower|reduce|aumenta|disminuye|duplica|reduce)\s+(?:the\s+|your\s+|la\s+|tu\s+)?(?:dose|dosage|dosis)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex DosageChangeRegex();

    [GeneratedRegex(
        @"\b(?:change|switch|replace|cambia|reemplaza)\s+(?:the\s+|your\s+|la\s+|tu\s+)?(?:medication|medicine|drug|medicamento|medicina)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex MedicationChangeRegex();

    [GeneratedRegex(
        @"\b(?:your\s+urgency\s+is|i\s+classify\s+(?:this|you)\s+as\s+(?:urgent|high\s+risk)|this\s+is\s+(?:(?:high|critical)\s+urgency|an?\s+emergency)|you\s+are\s+high\s+risk|tu\s+urgencia\s+es|clasifico\s+esto\s+como\s+urgente)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex AuthoritativeUrgencyRegex();

    [GeneratedRegex(
        @"\b(?:call\s+911|go\s+to\s+(?:the\s+)?(?:emergency\s+room|er)\s*(?:immediately|now)?|seek\s+emergency\s+care\s*(?:immediately|now)?|llama\s+al\s+911|ve\s+a\s+(?:la\s+)?(?:sala\s+de\s+emergencias|emergencia)\s+(?:inmediatamente|ahora))\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmergencyInstructionRegex();

    [GeneratedRegex(
        @"\b(?:treat\s+(?:this|it)\s+at\s+home|do\s+not\s+seek\s+medical\s+care|ignore\s+(?:these|the|your)\s+symptoms|there\s+is\s+no\s+need\s+to\s+see\s+(?:a\s+)?doctor|you\s+must\s+follow\s+this\s+treatment|trata(?:lo)?\s+en\s+casa|no\s+busques\s+atenci[oó]n\s+m[eé]dica|ignora\s+(?:estos|tus)\s+s[ií]ntomas)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeCareInstructionRegex();
}
