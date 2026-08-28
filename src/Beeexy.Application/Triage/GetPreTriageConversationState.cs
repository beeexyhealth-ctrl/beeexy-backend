using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class GetPreTriageConversationState(
    IClock clock,
    AuthorizePatientAccess authorizePatientAccess,
    IAnonymousPreTriageCapabilityService capabilityService,
    IClinicalDefinitionProvider definitionProvider,
    IPreTriageCompletionRepository repository,
    IPreTriageEducationalVideoCatalog? educationalVideos = null)
{
    public async Task<PreTriageConversationProjection> ExecuteAsync(
        GetPreTriageConversationStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var graph = await repository.GetAsync(query.SessionId, cancellationToken) ??
            throw new PreTriageSessionNotFoundException();
        await AuthorizeAsync(graph, query, cancellationToken);
        EnsureAvailable(graph, query.CallerMode, clock.UtcNow);

        var package = await definitionProvider.GetDefinitionByQuestionnaireIdAsync(
            graph.Session.QuestionnaireVersionId,
            cancellationToken) ?? throw new InvalidOperationException(
                "The session's pinned questionnaire package is unavailable.");
        EnsurePinnedPackage(graph, package);

        var answers = graph.Session.Status == PreTriageSessionStatus.Completed
            ? graph.Episode!.Answers
            : graph.Session.Answers;
        return PreTriageConversationProjectionBuilder.Build(
            graph.Session,
            answers,
            package,
            educationalVideos);
    }

    private async Task AuthorizeAsync(
        StoredPreTriageGraph graph,
        GetPreTriageConversationStateQuery query,
        CancellationToken cancellationToken)
    {
        var session = graph.Session;
        if (query.CallerMode == PreTriageCallerMode.Anonymous)
        {
            if (!session.IsAnonymous || session.AnonymousCapabilityHash is null ||
                !capabilityService.Verify(
                    query.AnonymousCapability,
                    session.AnonymousCapabilityHash))
            {
                throw new SessionAuthenticationException();
            }

            return;
        }

        var patientProfileId = session.PatientProfileId ??
            (graph.Episode?.IsClaimed == true
                ? graph.Episode.PatientProfileId
                : null);
        if (patientProfileId is null)
        {
            throw new PreTriageSessionNotFoundException();
        }

        var authorization = await authorizePatientAccess.ExecuteAsync(
            patientProfileId.Value,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new PreTriageSessionNotFoundException();
        }
    }

    private static void EnsureAvailable(
        StoredPreTriageGraph graph,
        PreTriageCallerMode callerMode,
        DateTimeOffset now)
    {
        if (graph.Session.Status == PreTriageSessionStatus.Active)
        {
            if (now >= graph.Session.ExpiresAt)
            {
                throw new PreTriageSessionNotFoundException();
            }

            return;
        }

        if (graph.Episode is null || graph.Assessment is null)
        {
            throw new InvalidOperationException(
                "The completed pre-triage graph is inconsistent.");
        }

        if (graph.Session.IsAnonymous &&
            (callerMode == PreTriageCallerMode.Anonymous ||
             graph.Episode.IsClaimed is false) &&
            (!graph.Episode.AnonymousExpiresAt.HasValue ||
             now >= graph.Episode.AnonymousExpiresAt.Value))
        {
            throw new PreTriageSessionNotFoundException();
        }
    }

    private static void EnsurePinnedPackage(
        StoredPreTriageGraph graph,
        ClinicalDefinitionPackage package)
    {
        if (package.Profile != ClinicalDefinitionPackageProfile.SimplifiedDemoIntake ||
            package.RuleDefinitions.DemoIntake is null ||
            package.Questionnaire.Id != graph.Session.QuestionnaireVersionId ||
            (graph.Episode is not null &&
                (graph.Episode.QuestionnaireVersionId != package.Questionnaire.Id ||
                 graph.Episode.ClinicalRuleSetVersionId != package.RuleSet.Id)))
        {
            throw new InvalidOperationException(
                "The session's pinned questionnaire package is inconsistent.");
        }
    }
}

public static class PreTriageConversationProjectionBuilder
{
    private static readonly IReadOnlyList<string> DurationUnits =
        ["MINUTES", "HOURS", "DAYS", "WEEKS", "MONTHS"];

    public static PreTriageConversationProjection Build(
        PreTriageSession session,
        IReadOnlyCollection<TriageAnswer> answers,
        ClinicalDefinitionPackage package,
        IPreTriageEducationalVideoCatalog? educationalVideos = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(package);
        var demo = package.RuleDefinitions.DemoIntake ?? throw new InvalidOperationException(
            "A conversation projection requires a simplified demo package.");
        if (package.Profile != ClinicalDefinitionPackageProfile.SimplifiedDemoIntake ||
            package.Questionnaire.Id != session.QuestionnaireVersionId)
        {
            throw new InvalidOperationException(
                "The conversation projection package is not pinned to the session.");
        }

        var codeByQuestionId = package.Questionnaire.Questions.ToDictionary(
            question => question.Id,
            question => question.Code);
        var acceptedValues = answers
            .OrderBy(answer => answer.Sequence)
            .Select(answer =>
            {
                if (answer.QuestionnaireVersionId != session.QuestionnaireVersionId ||
                    !codeByQuestionId.TryGetValue(answer.QuestionId, out var code) ||
                    !demo.ProgressionQuestionCodes.Contains(code))
                {
                    throw new InvalidOperationException(
                        "The stored answer is outside the pinned conversation questionnaire.");
                }

                return new AcceptedTriageAnswerValue(
                    code,
                    DemoTriageAnswerCodec.Decode(answer.AnswerJson, code, package));
            })
            .ToArray();
        var answeredCodes = acceptedValues.Select(value => value.Code).ToArray();
        var progression = SubmitTriageAnswers.ResolveProgression(answeredCodes, package);
        var required = demo.RequiredAnswerQuestionCodes.ToHashSet();
        var completed = answeredCodes.Distinct().Count(required.Contains);
        var total = required.Count;
        var percentage = total == 0
            ? 100
            : (int)Math.Round(
                completed * 100m / total,
                MidpointRounding.AwayFromZero);
        var state = session.Status == PreTriageSessionStatus.Completed
            ? PreTriageConversationState.Completed
            : session.EducationalVideoOfferRequired &&
                !session.EducationalVideoDecision.HasValue
                ? PreTriageConversationState.InProgress
                : progression.ReadyToComplete
                ? PreTriageConversationState.ReadyForReview
                : PreTriageConversationState.InProgress;
        PreTriageConversationInteraction? nextInteraction = null;
        if (state == PreTriageConversationState.InProgress)
        {
            if (session.EducationalVideoOfferRequired &&
                !session.EducationalVideoDecision.HasValue)
            {
                var video = educationalVideos?.Find(package.Pathway) ??
                    throw new InvalidOperationException(
                        "The session's educational video configuration is unavailable.");
                nextInteraction = BuildEducationalVideoOffer(video);
            }
            else
            {
                nextInteraction = BuildNextInteraction(
                    progression.NextQuestion,
                    package,
                    required);
            }
        }

        return new PreTriageConversationProjection(
            session.Id,
            session.Status,
            state,
            session.ExpiresAt,
            new PreTriageConversationPathway(
                package.Pathway,
                demo.PrimarySymptomDisplayLabel),
            new PreTriageConversationDefinitionReference(
                package.Questionnaire.QuestionnaireCode.Value,
                package.Questionnaire.Version),
            new PreTriageConversationDefinitionReference(
                package.RuleSet.RuleSetCode.Value,
                package.RuleSet.Version),
            new PreTriageConversationProgress(completed, total, percentage),
            acceptedValues,
            nextInteraction);
    }

    private static PreTriageConversationInteraction BuildNextInteraction(
        DemoNextQuestion? next,
        ClinicalDefinitionPackage package,
        IReadOnlySet<QuestionCode> required)
    {
        if (next is null)
        {
            throw new InvalidOperationException(
                "An in-progress conversation requires a next question.");
        }

        var demo = package.RuleDefinitions.DemoIntake!;
        if (next.Code == demo.DurationQuestionCode &&
            next.AnswerType == ClinicalAnswerType.Duration)
        {
            return new PreTriageConversationInteraction(
                PreTriageConversationInteractionType.Question,
                "duration",
                next.Code,
                next.Prompt,
                PreTriageConversationInputType.Duration,
                required.Contains(next.Code),
                new PreTriageConversationConstraints(
                    Minimum: 0,
                    Maximum: null,
                    Step: null,
                    ExclusiveMinimum: true,
                    AllowedUnits: DurationUnits,
                    MinimumSelections: null,
                    MaximumSelections: null,
                    AllowsEmptySelection: null),
                []);
        }

        if (next.Code == demo.IntensityQuestionCode &&
            next.AnswerType == ClinicalAnswerType.IntegerScale)
        {
            return new PreTriageConversationInteraction(
                PreTriageConversationInteractionType.Question,
                "intensity",
                next.Code,
                next.Prompt,
                PreTriageConversationInputType.Scale,
                required.Contains(next.Code),
                new PreTriageConversationConstraints(
                    next.Minimum,
                    next.Maximum,
                    1,
                    ExclusiveMinimum: null,
                    AllowedUnits: null,
                    MinimumSelections: null,
                    MaximumSelections: null,
                    AllowsEmptySelection: null),
                []);
        }

        if (next.Code == demo.AdditionalSymptomsQuestionCode &&
            next.AnswerType == ClinicalAnswerType.MultipleChoice)
        {
            var options = next.AllowedValues.Select(value =>
                new PreTriageConversationOption(
                    value,
                    DemoAdditionalSymptoms.DisplayLabel(value))).ToArray();
            return new PreTriageConversationInteraction(
                PreTriageConversationInteractionType.Question,
                "additionalSymptoms",
                next.Code,
                next.Prompt,
                PreTriageConversationInputType.MultiSelect,
                required.Contains(next.Code),
                new PreTriageConversationConstraints(
                    Minimum: null,
                    Maximum: null,
                    Step: null,
                    ExclusiveMinimum: null,
                    AllowedUnits: null,
                    MinimumSelections: demo.AdditionalSymptomsAllowsEmptySelection ? 0 : 1,
                    MaximumSelections: options.Length,
                    AllowsEmptySelection: demo.AdditionalSymptomsAllowsEmptySelection),
                options);
        }

        throw new InvalidOperationException(
            "The pinned questionnaire contains an unsupported conversation input type.");
    }

    private static PreTriageConversationInteraction BuildEducationalVideoOffer(
        PreTriageEducationalVideo video) => new(
            PreTriageConversationInteractionType.EducationalVideoOffer,
            "educationalVideoDecision",
            QuestionCode: null,
            "Would you like to watch a short video where a medical professional explains more about your symptoms?",
            PreTriageConversationInputType.SingleSelect,
            Required: false,
            new PreTriageConversationConstraints(
                Minimum: null,
                Maximum: null,
                Step: null,
                ExclusiveMinimum: null,
                AllowedUnits: null,
                MinimumSelections: 1,
                MaximumSelections: 1,
                AllowsEmptySelection: false),
            [
                new PreTriageConversationOption("WATCH", "Yes, show me the video"),
                new PreTriageConversationOption("SKIP", "No, continue with assessment")
            ],
            video);

}

public sealed record GetPreTriageConversationStateQuery(
    EntityId SessionId,
    PreTriageCallerMode CallerMode,
    string? AnonymousCapability);

public enum PreTriageConversationState
{
    InProgress,
    ReadyForReview,
    Completed
}

public enum PreTriageConversationInputType
{
    Duration,
    Scale,
    MultiSelect,
    SingleSelect
}

public enum PreTriageConversationInteractionType
{
    Question,
    EducationalVideoOffer
}

public sealed record PreTriageConversationProjection(
    EntityId SessionId,
    PreTriageSessionStatus SessionStatus,
    PreTriageConversationState State,
    DateTimeOffset ExpiresAt,
    PreTriageConversationPathway Pathway,
    PreTriageConversationDefinitionReference Questionnaire,
    PreTriageConversationDefinitionReference RuleSet,
    PreTriageConversationProgress Progress,
    IReadOnlyList<AcceptedTriageAnswerValue> AcceptedValues,
    PreTriageConversationInteraction? NextInteraction);

public sealed record PreTriageConversationPathway(
    ClinicalPathwayCode Code,
    string Label);

public sealed record PreTriageConversationDefinitionReference(
    string Code,
    DefinitionVersion Version);

public sealed record PreTriageConversationProgress(
    int Completed,
    int Total,
    int Percentage);

public sealed record PreTriageConversationInteraction(
    PreTriageConversationInteractionType Type,
    string Field,
    QuestionCode? QuestionCode,
    string Prompt,
    PreTriageConversationInputType InputType,
    bool Required,
    PreTriageConversationConstraints Constraints,
    IReadOnlyList<PreTriageConversationOption> Options,
    PreTriageEducationalVideo? Video = null);

public sealed record PreTriageConversationConstraints(
    decimal? Minimum,
    decimal? Maximum,
    decimal? Step,
    bool? ExclusiveMinimum,
    IReadOnlyList<string>? AllowedUnits,
    int? MinimumSelections,
    int? MaximumSelections,
    bool? AllowsEmptySelection);

public sealed record PreTriageConversationOption(
    string Value,
    string Label);
