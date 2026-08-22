using System.Text.Json;
using System.Text.Json.Nodes;
using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class SubmitTriageAnswers(
    IClock clock,
    AuthorizePatientAccess authorizePatientAccess,
    IAnonymousPreTriageCapabilityService capabilityService,
    IPreTriageAnswerRepository repository,
    IClinicalDefinitionProvider definitionProvider,
    InterpretClinicalInput interpretClinicalInput,
    IPreTriageIntakeAuditLogger auditLogger)
{
    public const int MaximumNaturalLanguageLength = 4000;

    public async Task<SubmitTriageAnswersResult> ExecuteAsync(
        SubmitTriageAnswersCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var initial = await repository.GetAsync(command.SessionId, cancellationToken) ??
            throw new PreTriageSessionNotFoundException();
        await AuthorizeAsync(initial, command, cancellationToken);
        EnsureMutable(initial, clock.UtcNow);
        ValidateInputMode(command);
        var package = await definitionProvider.GetDefinitionByQuestionnaireIdAsync(
            initial.QuestionnaireVersionId,
            cancellationToken) ?? throw new InvalidOperationException(
                "The session's pinned questionnaire package is unavailable.");
        EnsureUsablePackage(initial, package, command.QuestionnaireVersion);

        var knownFacts = DemoTriageAnswerCodec.DecodeKnownFacts(initial, package);
        IReadOnlyList<ClinicalAiValidatedFactCandidate> acceptedCandidates;
        TriageIntakeSubmissionOutcome outcome;
        ClinicalIntentClassification? clarificationClassification = null;
        string? clarificationCode = null;

        if (command.Structured is not null)
        {
            acceptedCandidates = ValidateStructured(command.Structured, package);
            outcome = TriageIntakeSubmissionOutcome.Accepted;
        }
        else
        {
            var interpretation = await interpretClinicalInput.ExecuteAsync(
                new ClinicalAiInterpretationRequest(
                    command.NaturalLanguage!,
                    package.Pathway,
                    knownFacts,
                    package.RuleDefinitions.DemoIntake!.ProgressionQuestionCodes,
                    package),
                cancellationToken);
            acceptedCandidates = SelectAcceptedCandidates(interpretation);
            (outcome, clarificationClassification, clarificationCode) =
                MapInterpretation(interpretation);
        }

        auditLogger.InterpretationEvaluated(
            command.SessionId,
            command.Structured is null,
            outcome,
            acceptedCandidates.Select(value => value.Code).Distinct().Count());

        var mutation = await repository.MutateLockedAsync(
            command.SessionId,
            async session =>
            {
                await AuthorizeAsync(session, command, cancellationToken);
                EnsureMutable(session, clock.UtcNow);
                if (session.QuestionnaireVersionId != package.Questionnaire.Id)
                {
                    throw new PreTriageSessionStateConflictException(
                        "The session questionnaire changed after it was read.");
                }

                var acceptedCodes = ApplyCandidates(session, package, acceptedCandidates,
                    clock.UtcNow);
                return new IntakeMutationResult(
                    acceptedCodes,
                    ResolveProgression(session, package));
            },
            cancellationToken) ?? throw new PreTriageSessionNotFoundException();

        auditLogger.AnswersProcessed(
            command.SessionId,
            outcome,
            mutation.AcceptedAnswerCodes.Count,
            mutation.Progression.ReadyToComplete);
        return new SubmitTriageAnswersResult(
            command.SessionId,
            package.Pathway,
            package.Version,
            outcome,
            mutation.AcceptedAnswerCodes,
            mutation.Progression,
            clarificationClassification,
            clarificationCode);
    }

    private static void ValidateInputMode(SubmitTriageAnswersCommand command)
    {
        var hasStructured = command.Structured is not null;
        var hasNatural = !string.IsNullOrWhiteSpace(command.NaturalLanguage);
        if (hasStructured == hasNatural || command.UnsupportedFields.Count > 0)
        {
            throw Invalid("pre_triage.answer_input_invalid",
                "Provide either structured answers or natural-language input, but not both.");
        }

        if (hasNatural && command.NaturalLanguage!.Length > MaximumNaturalLanguageLength)
        {
            throw Invalid("pre_triage.natural_language_invalid",
                "Natural-language input is too long.");
        }
    }

    private async Task AuthorizeAsync(
        PreTriageSession session,
        SubmitTriageAnswersCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CallerMode == PreTriageCallerMode.Anonymous)
        {
            if (!session.IsAnonymous || session.AnonymousCapabilityHash is null ||
                !capabilityService.Verify(
                    command.AnonymousCapability,
                    session.AnonymousCapabilityHash))
            {
                throw new SessionAuthenticationException();
            }

            return;
        }

        if (session.PatientProfileId is null)
        {
            throw new PreTriageSessionNotFoundException();
        }

        var authorization = await authorizePatientAccess.ExecuteAsync(
            session.PatientProfileId.Value,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new PreTriageSessionNotFoundException();
        }
    }

    private static void EnsureMutable(PreTriageSession session, DateTimeOffset now)
    {
        if (session.Status != PreTriageSessionStatus.Active)
        {
            throw new PreTriageSessionStateConflictException(
                "A completed pre-triage session cannot accept answers.");
        }

        if (now >= session.ExpiresAt)
        {
            throw new PreTriageSessionStateConflictException(
                "An expired pre-triage session cannot accept answers.");
        }
    }

    private static void EnsureUsablePackage(
        PreTriageSession session,
        ClinicalDefinitionPackage package,
        string? requestedVersion)
    {
        if (package.Profile != ClinicalDefinitionPackageProfile.SimplifiedDemoIntake ||
            package.Questionnaire.Id != session.QuestionnaireVersionId ||
            package.RuleDefinitions.DemoIntake is null)
        {
            throw new InvalidOperationException(
                "The session is not pinned to a usable simplified demo package.");
        }

        if (requestedVersion is not null &&
            !string.Equals(requestedVersion, package.Version.Value, StringComparison.Ordinal))
        {
            throw Invalid("pre_triage.questionnaire_version_mismatch",
                "The submitted questionnaire version does not match the session.");
        }
    }

    private static IReadOnlyList<ClinicalAiValidatedFactCandidate> ValidateStructured(
        StructuredTriageAnswerInput input,
        ClinicalDefinitionPackage package)
    {
        if (input.UnsupportedFields.Count > 0)
        {
            throw Invalid("pre_triage.answer_input_invalid",
                "The structured answer contains an unsupported field.");
        }

        var demo = package.RuleDefinitions.DemoIntake!;
        var candidates = new List<ClinicalAiFactCandidate>(3);
        if (input.Duration is not null)
        {
            if (input.Duration.UnsupportedFields.Count > 0 ||
                !TryParseDurationUnit(input.Duration.Unit, out var unit))
            {
                throw Invalid("pre_triage.duration_invalid",
                    "Duration requires a positive value and supported unit.");
            }

            candidates.Add(new ClinicalAiFactCandidate(
                demo.DurationQuestionCode,
                new ClinicalAiDurationValue(input.Duration.Value, unit),
                ClinicalAiConfidenceSignal.Sufficient));
        }

        if (input.Intensity.HasValue)
        {
            candidates.Add(new ClinicalAiFactCandidate(
                demo.IntensityQuestionCode,
                new ClinicalAiIntegerValue(input.Intensity.Value),
                ClinicalAiConfidenceSignal.Sufficient));
        }

        if (input.AdditionalSymptoms is not null)
        {
            candidates.Add(new ClinicalAiFactCandidate(
                demo.AdditionalSymptomsQuestionCode,
                new ClinicalAiMultipleChoiceValue(input.AdditionalSymptoms),
                ClinicalAiConfidenceSignal.Sufficient));
        }

        if (candidates.Count == 0)
        {
            throw Invalid("pre_triage.answer_required",
                "At least one structured answer is required.");
        }

        var questions = package.Questions.ToDictionary(value => value.Code);
        foreach (var candidate in candidates)
        {
            var issue = ClinicalAnswerValueValidator.Validate(
                candidate.Value,
                questions[candidate.Code],
                package);
            if (issue.HasValue)
            {
                throw Invalid(StructuredIssueCode(candidate.Code, demo),
                    "The structured answer is invalid for the pinned questionnaire.");
            }
        }

        return candidates.Select(value => new ClinicalAiValidatedFactCandidate(
            value.Code,
            value.Value,
            ClinicalAiCandidateStatus.AcceptedCandidate)).ToArray();
    }

    private static string StructuredIssueCode(
        QuestionCode code,
        DemoIntakePackageDefinition demo)
    {
        if (code == demo.DurationQuestionCode)
        {
            return "pre_triage.duration_invalid";
        }

        return code == demo.IntensityQuestionCode
            ? "pre_triage.intensity_invalid"
            : "pre_triage.additional_symptoms_invalid";
    }

    private static bool TryParseDurationUnit(
        string? value,
        out ClinicalDurationUnit unit)
    {
        unit = value switch
        {
            "MINUTES" => ClinicalDurationUnit.Minutes,
            "HOURS" => ClinicalDurationUnit.Hours,
            "DAYS" => ClinicalDurationUnit.Days,
            "WEEKS" => ClinicalDurationUnit.Weeks,
            "MONTHS" => ClinicalDurationUnit.Months,
            _ => (ClinicalDurationUnit)(-1)
        };
        return Enum.IsDefined(unit);
    }

    private static IReadOnlyList<ClinicalAiValidatedFactCandidate> SelectAcceptedCandidates(
        ClinicalAiInterpretationResult interpretation)
    {
        if (interpretation.Outcome is not ClinicalAiInterpretationOutcome.Accepted and
            not ClinicalAiInterpretationOutcome.ClarificationRequired)
        {
            return [];
        }

        return interpretation.Validation?.Facts
            .Where(value => value.Status == ClinicalAiCandidateStatus.AcceptedCandidate)
            .GroupBy(value => value.Code)
            .Select(value => value.First())
            .ToArray() ?? [];
    }

    private static (
        TriageIntakeSubmissionOutcome Outcome,
        ClinicalIntentClassification? Classification,
        string? ClarificationCode) MapInterpretation(
        ClinicalAiInterpretationResult interpretation)
    {
        return interpretation.Outcome switch
        {
            ClinicalAiInterpretationOutcome.Accepted =>
                (TriageIntakeSubmissionOutcome.Accepted, null, null),
            ClinicalAiInterpretationOutcome.ClarificationRequired =>
                (TriageIntakeSubmissionOutcome.ClarificationRequired,
                    interpretation.SafetyClassification,
                    "CLARIFICATION_REQUIRED"),
            ClinicalAiInterpretationOutcome.SafetyRestricted =>
                (TriageIntakeSubmissionOutcome.SafetyRestricted,
                    interpretation.SafetyClassification,
                    "SAFETY_RESTRICTED"),
            ClinicalAiInterpretationOutcome.Unsupported =>
                (TriageIntakeSubmissionOutcome.Unsupported,
                    interpretation.SafetyClassification,
                    "UNSUPPORTED_INPUT"),
            ClinicalAiInterpretationOutcome.ProviderUnavailable or
                ClinicalAiInterpretationOutcome.ProviderTimeout or
                ClinicalAiInterpretationOutcome.ConfigurationUnavailable =>
                (TriageIntakeSubmissionOutcome.ProviderUnavailable,
                    null,
                    "INTERPRETATION_UNAVAILABLE"),
            _ => (TriageIntakeSubmissionOutcome.ClarificationRequired,
                null,
                "INVALID_INTERPRETATION")
        };
    }

    private static IReadOnlyList<QuestionCode> ApplyCandidates(
        PreTriageSession session,
        ClinicalDefinitionPackage package,
        IReadOnlyList<ClinicalAiValidatedFactCandidate> candidates,
        DateTimeOffset recordedAt)
    {
        var questionEntities = package.Questionnaire.Questions.ToDictionary(value => value.Code);
        var existingByQuestion = session.Answers.ToDictionary(value => value.QuestionId);
        var pending = candidates.Select(candidate =>
        {
            var question = questionEntities[candidate.Code];
            return new
            {
                candidate.Code,
                Question = question,
                Json = DemoTriageAnswerCodec.Encode(candidate.Value, candidate.Code, package),
                Existing = existingByQuestion.GetValueOrDefault(question.Id)
            };
        }).ToArray();

        foreach (var item in pending)
        {
            if (item.Existing is not null &&
                !JsonAnswersEqual(item.Existing.AnswerJson, item.Json))
            {
                throw new PreTriageSessionStateConflictException(
                    "An answer already exists with a different value.");
            }
        }

        foreach (var item in pending.Where(value => value.Existing is null))
        {
            session.RecordAnswer(
                item.Question,
                item.Json,
                item.Question.DisplayOrder,
                recordedAt);
        }

        return pending.Select(value => value.Code).Distinct().ToArray();
    }

    private static bool JsonAnswersEqual(string existing, string submitted) =>
        JsonNode.DeepEquals(JsonNode.Parse(existing), JsonNode.Parse(submitted));

    private static DemoQuestionnaireProgress ResolveProgression(
        PreTriageSession session,
        ClinicalDefinitionPackage package)
    {
        var demo = package.RuleDefinitions.DemoIntake!;
        var entityCodeById = package.Questionnaire.Questions.ToDictionary(
            value => value.Id,
            value => value.Code);
        var answered = session.Answers
            .Select(value => entityCodeById[value.QuestionId])
            .ToHashSet();
        var answeredRequired = demo.ProgressionQuestionCodes
            .Where(answered.Contains)
            .ToArray();
        var missing = demo.RequiredAnswerQuestionCodes
            .Where(value => !answered.Contains(value))
            .ToArray();
        var nextCode = demo.ProgressionQuestionCodes.FirstOrDefault(
            value => !answered.Contains(value));
        DemoNextQuestion? next = null;
        if (nextCode is not null)
        {
            var question = package.Questions.Single(value => value.Code == nextCode);
            next = new DemoNextQuestion(
                question.Code,
                question.PromptText,
                question.Answer.Type,
                question.Answer.AllowedValues ?? [],
                question.Answer.Type == ClinicalAnswerType.Duration
                    ? ["MINUTES", "HOURS", "DAYS", "WEEKS", "MONTHS"]
                    : [],
                question.Answer.Minimum,
                question.Answer.Maximum);
        }

        return new DemoQuestionnaireProgress(
            missing.Length == 0
                ? DemoQuestionnaireProgressState.ReadyToComplete
                : DemoQuestionnaireProgressState.InProgress,
            answeredRequired,
            missing,
            next,
            missing.Length == 0);
    }

    private static RequestValidationException Invalid(string code, string message) =>
        new(code, message);

    private sealed record IntakeMutationResult(
        IReadOnlyList<QuestionCode> AcceptedAnswerCodes,
        DemoQuestionnaireProgress Progression);
}

internal static class DemoTriageAnswerCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Encode(
        ClinicalAiCandidateValue value,
        QuestionCode code,
        ClinicalDefinitionPackage package)
    {
        var demo = package.RuleDefinitions.DemoIntake!;
        if (code == demo.DurationQuestionCode && value is ClinicalAiDurationValue duration)
        {
            return JsonSerializer.Serialize(
                new DurationPayload(duration.Value, DurationUnitCode(duration.Unit)), Options);
        }

        if (code == demo.IntensityQuestionCode && value is ClinicalAiIntegerValue intensity)
        {
            return JsonSerializer.Serialize(new IntensityPayload(intensity.Value), Options);
        }

        if (code == demo.AdditionalSymptomsQuestionCode &&
            value is ClinicalAiMultipleChoiceValue additional)
        {
            var order = demo.ApplicableAdditionalSymptoms
                .Select((item, index) => (item, index))
                .ToDictionary(value => value.item, value => value.index, StringComparer.Ordinal);
            var ordered = additional.Values.OrderBy(value => order[value]).ToArray();
            return JsonSerializer.Serialize(new AdditionalSymptomsPayload(ordered), Options);
        }

        throw new InvalidOperationException("The answer cannot be encoded for this demo field.");
    }

    public static IReadOnlyList<ClinicalAiKnownFact> DecodeKnownFacts(
        PreTriageSession session,
        ClinicalDefinitionPackage package)
    {
        var codeById = package.Questionnaire.Questions.ToDictionary(
            value => value.Id,
            value => value.Code);
        return session.Answers.Select(answer =>
        {
            var code = codeById[answer.QuestionId];
            return new ClinicalAiKnownFact(code, Decode(answer.AnswerJson, code, package));
        }).ToArray();
    }

    private static ClinicalAiCandidateValue Decode(
        string json,
        QuestionCode code,
        ClinicalDefinitionPackage package)
    {
        var demo = package.RuleDefinitions.DemoIntake!;
        if (code == demo.DurationQuestionCode)
        {
            var value = JsonSerializer.Deserialize<DurationPayload>(json, Options) ??
                throw new InvalidOperationException("Stored duration answer is invalid.");
            return new ClinicalAiDurationValue(
                value.Value,
                value.Unit switch
                {
                    "MINUTES" => ClinicalDurationUnit.Minutes,
                    "HOURS" => ClinicalDurationUnit.Hours,
                    "DAYS" => ClinicalDurationUnit.Days,
                    "WEEKS" => ClinicalDurationUnit.Weeks,
                    "MONTHS" => ClinicalDurationUnit.Months,
                    _ => throw new InvalidOperationException(
                        "Stored duration unit is invalid.")
                });
        }

        if (code == demo.IntensityQuestionCode)
        {
            var value = JsonSerializer.Deserialize<IntensityPayload>(json, Options) ??
                throw new InvalidOperationException("Stored intensity answer is invalid.");
            return new ClinicalAiIntegerValue(value.Value);
        }

        if (code == demo.AdditionalSymptomsQuestionCode)
        {
            var value = JsonSerializer.Deserialize<AdditionalSymptomsPayload>(json, Options) ??
                throw new InvalidOperationException("Stored additional-symptom answer is invalid.");
            return new ClinicalAiMultipleChoiceValue(value.Values);
        }

        throw new InvalidOperationException("Stored answer is outside the demo questionnaire.");
    }

    private static string DurationUnitCode(ClinicalDurationUnit unit) => unit switch
    {
        ClinicalDurationUnit.Minutes => "MINUTES",
        ClinicalDurationUnit.Hours => "HOURS",
        ClinicalDurationUnit.Days => "DAYS",
        ClinicalDurationUnit.Weeks => "WEEKS",
        ClinicalDurationUnit.Months => "MONTHS",
        _ => throw new InvalidOperationException("Duration unit is invalid.")
    };

    private sealed record DurationPayload(decimal Value, string Unit);

    private sealed record IntensityPayload(int Value);

    private sealed record AdditionalSymptomsPayload(IReadOnlyList<string> Values);
}

public sealed record SubmitTriageAnswersCommand(
    EntityId SessionId,
    PreTriageCallerMode CallerMode,
    string? AnonymousCapability,
    string? QuestionnaireVersion,
    StructuredTriageAnswerInput? Structured,
    string? NaturalLanguage,
    IReadOnlyCollection<string> UnsupportedFields);

public sealed record StructuredTriageAnswerInput(
    DurationTriageAnswerInput? Duration,
    int? Intensity,
    IReadOnlyList<string>? AdditionalSymptoms,
    IReadOnlyCollection<string> UnsupportedFields);

public sealed record DurationTriageAnswerInput(
    decimal Value,
    string? Unit,
    IReadOnlyCollection<string> UnsupportedFields);

public enum TriageIntakeSubmissionOutcome
{
    Accepted,
    ClarificationRequired,
    SafetyRestricted,
    Unsupported,
    ProviderUnavailable
}

public enum DemoQuestionnaireProgressState
{
    InProgress,
    ReadyToComplete
}

public sealed record DemoNextQuestion(
    QuestionCode Code,
    string Prompt,
    ClinicalAnswerType AnswerType,
    IReadOnlyList<string> AllowedValues,
    IReadOnlyList<string> AllowedUnits,
    decimal? Minimum,
    decimal? Maximum);

public sealed record DemoQuestionnaireProgress(
    DemoQuestionnaireProgressState State,
    IReadOnlyList<QuestionCode> AnsweredRequiredFields,
    IReadOnlyList<QuestionCode> MissingRequiredFields,
    DemoNextQuestion? NextQuestion,
    bool ReadyToComplete);

public sealed record SubmitTriageAnswersResult(
    EntityId SessionId,
    ClinicalPathwayCode Pathway,
    DefinitionVersion QuestionnaireVersion,
    TriageIntakeSubmissionOutcome Outcome,
    IReadOnlyList<QuestionCode> AcceptedAnswerCodes,
    DemoQuestionnaireProgress Progression,
    ClinicalIntentClassification? ClarificationClassification,
    string? ClarificationCode);

public interface IPreTriageAnswerRepository
{
    Task<PreTriageSession?> GetAsync(
        EntityId sessionId,
        CancellationToken cancellationToken = default);

    Task<TResult?> MutateLockedAsync<TResult>(
        EntityId sessionId,
        Func<PreTriageSession, Task<TResult>> mutation,
        CancellationToken cancellationToken = default)
        where TResult : class;
}

public interface IPreTriageIntakeAuditLogger
{
    void InterpretationEvaluated(
        EntityId sessionId,
        bool usedNaturalLanguage,
        TriageIntakeSubmissionOutcome outcome,
        int acceptedCandidateCategoryCount);

    void AnswersProcessed(
        EntityId sessionId,
        TriageIntakeSubmissionOutcome outcome,
        int acceptedAnswerCategoryCount,
        bool readyToComplete);
}

public sealed class PreTriageSessionNotFoundException : Exception;

public sealed class PreTriageSessionStateConflictException(string message)
    : InvalidOperationException(message);
