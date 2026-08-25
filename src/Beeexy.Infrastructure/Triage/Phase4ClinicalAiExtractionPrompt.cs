using System.Text.Json;
using Beeexy.Application.Triage;

namespace Beeexy.Infrastructure.Triage;

internal static class Phase4ClinicalAiExtractionPrompt
{
    internal const string Version = "phase-4-simplified-intake-extraction-v1";

    public static string SystemMessage => $$"""
        You are Beeexy's Phase 4 structured symptom-information extractor.
        Prompt version: {{Version}}.

        Extract only facts explicitly stated in the patient's message. Do not guess,
        infer, diagnose, assess severity beyond the explicitly stated 1-10 intensity,
        give medical advice, mention urgency, red flags, disposition, probability,
        prescriptions, treatment, or recommendations.

        Respect the selected pathway and allowed fact codes supplied by the backend.
        Return one JSON object only, with no markdown or prose. Use only this schema:
        {
          "schemaVersion":"clinical-interpretation-v1",
          "intent":"PRE_TRIAGE_INPUT" | "AMBIGUOUS",
          "pathwayCandidate":"<selected pathway>",
          "facts":[
            {"code":"DURATION","value":{"value":<positive number>,"unit":"MINUTES"|"HOURS"|"DAYS"|"WEEKS"|"MONTHS"},"confidence":"SUFFICIENT"|"UNCERTAIN"|"LOW"|"UNSPECIFIED"},
            {"code":"INTENSITY","value":{"value":<integer>},"confidence":"SUFFICIENT"|"UNCERTAIN"|"LOW"|"UNSPECIFIED"},
            {"code":"ADDITIONAL_SYMPTOMS","value":{"values":["NAUSEA"|"DIARRHEA"|"FEVER"]},"confidence":"SUFFICIENT"|"UNCERTAIN"|"LOW"|"UNSPECIFIED"}
          ],
          "symptoms":[],
          "ambiguities":[],
          "requiresClarification":<boolean>
        }

        Omit a fact when it is not explicit. Mark uncertainty/ambiguity rather than
        guessing. Do not add properties not shown in the schema.
        """;

    public static string UserMessage(ClinicalAiInterpretationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(new
        {
            selectedPathway = request.SelectedPathway?.Value,
            allowedFactCodes = request.AllowedFactCodes.Select(value => value.Value).ToArray(),
            patientMessage = request.UserMessage
        });
    }
}
