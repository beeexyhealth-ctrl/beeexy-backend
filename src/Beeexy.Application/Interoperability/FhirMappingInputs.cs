using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Interoperability;

public sealed record QuestionnaireResponseAnswerInput(
    EntityId AnswerId,
    EntityId QuestionId,
    string QuestionCode,
    string PromptText,
    int DisplayOrder,
    string? AnswerSchemaJson,
    string AnswerJson,
    DateTimeOffset RecordedAt);

public sealed record QuestionnaireResponseSymptomInput(
    EntityId SymptomId,
    int Sequence,
    string OriginalText,
    string? TerminologySystem,
    string? TerminologyCode,
    string? TerminologyDisplay,
    DateTimeOffset ReportedAt);

public sealed class QuestionnaireResponseMappingInput
{
    private QuestionnaireResponseMappingInput(
        EntityId patientProfileId,
        EntityId sourceClinicalHistoryEventId,
        EntityId episodeId,
        EntityId questionnaireVersionId,
        string questionnaireCode,
        string questionnaireVersion,
        string questionnaireContentHash,
        DateTimeOffset authoredAt,
        IReadOnlyList<QuestionnaireResponseAnswerInput> answers,
        IReadOnlyList<QuestionnaireResponseSymptomInput> symptoms)
    {
        PatientProfileId = patientProfileId;
        SourceClinicalHistoryEventId = sourceClinicalHistoryEventId;
        EpisodeId = episodeId;
        QuestionnaireVersionId = questionnaireVersionId;
        QuestionnaireCode = questionnaireCode;
        QuestionnaireVersion = questionnaireVersion;
        QuestionnaireContentHash = questionnaireContentHash;
        AuthoredAt = authoredAt;
        Answers = answers;
        Symptoms = symptoms;
    }

    public EntityId PatientProfileId { get; }

    public EntityId SourceClinicalHistoryEventId { get; }

    public EntityId EpisodeId { get; }

    public EntityId QuestionnaireVersionId { get; }

    public string QuestionnaireCode { get; }

    public string QuestionnaireVersion { get; }

    public string QuestionnaireContentHash { get; }

    public DateTimeOffset AuthoredAt { get; }

    public IReadOnlyList<QuestionnaireResponseAnswerInput> Answers { get; }

    public IReadOnlyList<QuestionnaireResponseSymptomInput> Symptoms { get; }

    public static QuestionnaireResponseMappingInput Create(
        ClinicalHistoryEvent sourceEvent,
        PreTriageEpisode episode,
        QuestionnaireDefinitionVersion questionnaire)
    {
        ArgumentNullException.ThrowIfNull(questionnaire);
        FhirMappingInputGuard.EnsureSourceGraph(sourceEvent, episode);
        if (questionnaire.Id != episode.QuestionnaireVersionId)
        {
            throw new FhirMappingInputException(
                "The questionnaire definition does not match the episode's frozen version.");
        }

        if (episode.Answers.Count == 0)
        {
            throw new FhirMappingInputException(
                "A QuestionnaireResponse mapping input requires source answers.");
        }

        var questions = questionnaire.Questions.ToDictionary(question => question.Id);
        var answers = new List<QuestionnaireResponseAnswerInput>(episode.Answers.Count);
        foreach (var answer in episode.Answers.OrderBy(answer => answer.Sequence))
        {
            if (answer.EpisodeId != episode.Id ||
                answer.SessionId is not null ||
                answer.QuestionnaireVersionId != questionnaire.Id)
            {
                throw new FhirMappingInputException(
                    "An answer is not owned by the episode's frozen questionnaire version.");
            }

            if (!questions.TryGetValue(answer.QuestionId, out var question))
            {
                throw new FhirMappingInputException(
                    "An answer has no question in the episode's frozen questionnaire version.");
            }

            answers.Add(new QuestionnaireResponseAnswerInput(
                answer.Id,
                question.Id,
                question.Code.Value,
                question.PromptText,
                question.DisplayOrder,
                question.AnswerSchemaJson,
                answer.AnswerJson,
                answer.RecordedAt));
        }

        var symptoms = episode.ReportedSymptoms
            .OrderBy(symptom => symptom.Sequence)
            .Select(symptom =>
            {
                if (symptom.EpisodeId != episode.Id || symptom.SessionId is not null)
                {
                    throw new FhirMappingInputException(
                        "A reported symptom is not owned by the source episode.");
                }

                return new QuestionnaireResponseSymptomInput(
                    symptom.Id,
                    symptom.Sequence,
                    symptom.OriginalText.Value,
                    symptom.TerminologySystem,
                    symptom.TerminologyCode,
                    symptom.TerminologyDisplay,
                    symptom.ReportedAt);
            })
            .ToArray();

        return new QuestionnaireResponseMappingInput(
            sourceEvent.PatientProfileId,
            sourceEvent.Id,
            episode.Id,
            questionnaire.Id,
            questionnaire.QuestionnaireCode.Value,
            questionnaire.Version.Value,
            questionnaire.ContentHash.Value,
            episode.CompletedAt,
            answers.AsReadOnly(),
            symptoms);
    }
}

public sealed class RiskAssessmentMappingInput
{
    private static readonly IReadOnlyList<FhirUnresolvedMappingRequirement>
        NeutralAssessmentUnresolvedRequirements =
        [
            FhirUnresolvedMappingRequirement.RiskPredictionOutcome,
            FhirUnresolvedMappingRequirement.RiskPredictionProbability,
            FhirUnresolvedMappingRequirement.RiskMitigation
        ];

    private RiskAssessmentMappingInput(
        EntityId patientProfileId,
        EntityId sourceClinicalHistoryEventId,
        EntityId episodeId,
        EntityId assessmentId,
        EntityId clinicalRuleSetVersionId,
        DateTimeOffset occurrenceAt)
    {
        PatientProfileId = patientProfileId;
        SourceClinicalHistoryEventId = sourceClinicalHistoryEventId;
        EpisodeId = episodeId;
        AssessmentId = assessmentId;
        ClinicalRuleSetVersionId = clinicalRuleSetVersionId;
        OccurrenceAt = occurrenceAt;
    }

    public EntityId PatientProfileId { get; }

    public EntityId SourceClinicalHistoryEventId { get; }

    public EntityId EpisodeId { get; }

    public EntityId AssessmentId { get; }

    public EntityId ClinicalRuleSetVersionId { get; }

    public DateTimeOffset OccurrenceAt { get; }

    public bool IsResourceGenerationReady => false;

    public IReadOnlyList<FhirUnresolvedMappingRequirement> UnresolvedRequirements =>
        NeutralAssessmentUnresolvedRequirements;

    public static RiskAssessmentMappingInput Create(
        ClinicalHistoryEvent sourceEvent,
        PreTriageEpisode episode,
        ClinicalAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        FhirMappingInputGuard.EnsureSourceGraph(sourceEvent, episode);
        FhirMappingInputGuard.EnsureAssessment(episode, assessment);

        if (assessment.UrgencyCode is not null ||
            assessment.ResultMessageReference is not null ||
            assessment.Findings.Count != 0)
        {
            throw new FhirMappingInputException(
                "Only the current neutral assessment is supported; clinical conclusions cannot be reintroduced through FHIR mapping.");
        }

        return new RiskAssessmentMappingInput(
            sourceEvent.PatientProfileId,
            sourceEvent.Id,
            episode.Id,
            assessment.Id,
            assessment.ClinicalRuleSetVersionId,
            assessment.CreatedAt);
    }
}

public sealed record DeviceMappingInput
{
    public const int MaximumSoftwareVersionLength = 128;

    private DeviceMappingInput(string softwareVersion)
    {
        SoftwareVersion = softwareVersion;
    }

    public string DeviceName => AndreaFhirMappingInventory.DeviceName;

    public string DeviceNameType => AndreaFhirMappingInventory.DeviceNameType;

    public string ModelNumber => AndreaFhirMappingInventory.DeviceModelNumber;

    public string SoftwareVersion { get; }

    public string Manufacturer => AndreaFhirMappingInventory.DeviceManufacturer;

    public string TypeText => AndreaFhirMappingInventory.DeviceTypeText;

    public static DeviceMappingInput Create(string softwareVersion)
    {
        return new DeviceMappingInput(MappingText.Required(
            softwareVersion,
            MaximumSoftwareVersionLength,
            nameof(softwareVersion)));
    }
}

public sealed class ProvenanceMappingInput
{
    private ProvenanceMappingInput(
        EntityId patientProfileId,
        EntityId sourceClinicalHistoryEventId,
        EntityId sourceEpisodeId,
        EntityId sourceAssessmentId,
        FhirGenerationTrace generationTrace,
        FhirLogicalResourceIdentity target)
    {
        PatientProfileId = patientProfileId;
        SourceClinicalHistoryEventId = sourceClinicalHistoryEventId;
        SourceEpisodeId = sourceEpisodeId;
        SourceAssessmentId = sourceAssessmentId;
        GenerationTrace = generationTrace;
        Target = target;
    }

    public EntityId PatientProfileId { get; }

    public EntityId SourceClinicalHistoryEventId { get; }

    public EntityId SourceEpisodeId { get; }

    public EntityId SourceAssessmentId { get; }

    public FhirGenerationTrace GenerationTrace { get; }

    public FhirLogicalResourceIdentity Target { get; }

    public FhirLogicalResourceIdentity Agent => GenerationTrace.Device;

    public FhirLogicalResourceIdentity SourceEntity =>
        GenerationTrace.QuestionnaireResponse;

    public static ProvenanceMappingInput Create(
        ClinicalHistoryEvent sourceEvent,
        PreTriageEpisode episode,
        ClinicalAssessment assessment,
        FhirGenerationTrace generationTrace)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(generationTrace);
        FhirMappingInputGuard.EnsureSourceGraph(sourceEvent, episode);
        FhirMappingInputGuard.EnsureAssessment(episode, assessment);
        if (generationTrace.RecordedAt < assessment.CreatedAt)
        {
            throw new FhirMappingInputException(
                "FHIR generation provenance cannot precede its source assessment.");
        }

        return new ProvenanceMappingInput(
            sourceEvent.PatientProfileId,
            sourceEvent.Id,
            episode.Id,
            assessment.Id,
            generationTrace,
            generationTrace.RiskAssessment);
    }

    public static ProvenanceMappingInput CreateForQuestionnaireResponseTarget(
        ClinicalHistoryEvent sourceEvent,
        PreTriageEpisode episode,
        ClinicalAssessment assessment,
        FhirGenerationTrace generationTrace)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(generationTrace);
        FhirMappingInputGuard.EnsureSourceGraph(sourceEvent, episode);
        FhirMappingInputGuard.EnsureAssessment(episode, assessment);
        if (generationTrace.RecordedAt < assessment.CreatedAt)
        {
            throw new FhirMappingInputException(
                "FHIR generation provenance cannot precede its source assessment.");
        }

        return new ProvenanceMappingInput(
            sourceEvent.PatientProfileId,
            sourceEvent.Id,
            episode.Id,
            assessment.Id,
            generationTrace,
            generationTrace.QuestionnaireResponse);
    }
}

internal static class FhirMappingInputGuard
{
    public static void EnsureSourceGraph(
        ClinicalHistoryEvent sourceEvent,
        PreTriageEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(sourceEvent);
        ArgumentNullException.ThrowIfNull(episode);
        if (!episode.PatientProfileId.HasValue ||
            sourceEvent.EventType != ClinicalHistoryEventType.CompletedPreTriage ||
            sourceEvent.SourceType != AuthoritativeClinicalSourceType.PreTriageEpisode ||
            sourceEvent.PatientProfileId != episode.PatientProfileId.Value ||
            sourceEvent.SourceId != episode.Id ||
            sourceEvent.SourceQuestionnaireVersionId != episode.QuestionnaireVersionId ||
            sourceEvent.SourceClinicalRuleSetVersionId != episode.ClinicalRuleSetVersionId ||
            sourceEvent.OccurredAt != episode.CompletedAt)
        {
            throw new FhirMappingInputException(
                "The Clinical History source does not match the patient-owned completed episode.");
        }
    }

    public static void EnsureAssessment(
        PreTriageEpisode episode,
        ClinicalAssessment assessment)
    {
        if (assessment.EpisodeId != episode.Id ||
            assessment.ClinicalRuleSetVersionId != episode.ClinicalRuleSetVersionId ||
            assessment.CreatedAt < episode.CompletedAt)
        {
            throw new FhirMappingInputException(
                "The assessment does not match the episode and its frozen rule-set version.");
        }
    }
}
