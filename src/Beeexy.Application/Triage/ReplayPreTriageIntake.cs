using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class ReplayPreTriageIntake(
    AuthorizePatientAccess authorizePatientAccess,
    IAnonymousPreTriageCapabilityService capabilityService,
    IPreTriageIntakeReplayRepository repository,
    IClinicalDefinitionProvider definitionProvider,
    IPreTriageEducationalVideoCatalog? educationalVideos = null)
{
    public async Task<StartPreTriageFromIntakeResult> ExecuteAsync(
        ReplayPreTriageIntakeQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var state = await repository.LoadAsync(query.SessionId, cancellationToken) ??
            throw new PreTriageSessionNotFoundException();
        await AuthorizeAsync(state.Session, query, cancellationToken);
        var package = await definitionProvider.GetDefinitionByQuestionnaireIdAsync(
            state.Session.QuestionnaireVersionId,
            cancellationToken) ?? throw new InvalidOperationException(
                "The session's pinned questionnaire package is unavailable.");
        EnsureUsablePackage(state.Session, package);

        var initialCodes = query.InitialAnswerCodes
            .Select(QuestionCode.Create)
            .ToArray();
        var questionByCode = package.Questionnaire.Questions.ToDictionary(
            value => value.Code);
        var answerByQuestionId = state.Answers.ToDictionary(value => value.QuestionId);
        var acceptedValues = initialCodes.Select(code =>
        {
            if (!questionByCode.TryGetValue(code, out var question) ||
                !answerByQuestionId.TryGetValue(question.Id, out var answer))
            {
                throw new InvalidOperationException(
                    "The durable intake result no longer matches canonical answer state.");
            }

            return new AcceptedTriageAnswerValue(
                code,
                DemoTriageAnswerCodec.Decode(answer.AnswerJson, code, package));
        }).ToArray();

        var conversation = PreTriageConversationProjectionBuilder.Build(
            state.Session,
            state.Answers,
            package,
            educationalVideos);
        var session = new StartPreTriageResult(
            state.Session.Id,
            query.CallerMode == PreTriageCallerMode.Anonymous
                ? null
                : state.Session.PatientProfileId,
            package.Pathway,
            state.Session.Status,
            state.Session.ExpiresAt,
            package.Questionnaire.QuestionnaireCode,
            package.Questionnaire.Version,
            package.RuleSet.RuleSetCode,
            package.RuleSet.Version,
            package.ContentStatus,
            query.CallerMode == PreTriageCallerMode.Anonymous
                ? query.AnonymousCapability
                : null,
            state.Session.CreatedAt)
        {
            Conversation = conversation
        };
        var answers = new SubmitTriageAnswersResult(
            state.Session.Id,
            package.Pathway,
            package.Version,
            TriageIntakeSubmissionOutcome.Accepted,
            initialCodes,
            acceptedValues,
            SubmitTriageAnswers.ResolveProgression(initialCodes, package),
            null,
            null)
        {
            Conversation = conversation
        };
        return new StartPreTriageFromIntakeResult(
            PreTriageIntakeResolution.Resolved,
            [],
            session,
            answers);
    }

    private async Task AuthorizeAsync(
        PreTriageSession session,
        ReplayPreTriageIntakeQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CallerMode == PreTriageCallerMode.Anonymous)
        {
            if (string.IsNullOrWhiteSpace(query.AnonymousCapability))
            {
                throw new PreTriageAnonymousReplayCapabilityRequiredException();
            }

            if (!session.IsAnonymous || session.AnonymousCapabilityHash is null ||
                !capabilityService.Verify(
                    query.AnonymousCapability,
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

    private static void EnsureUsablePackage(
        PreTriageSession session,
        ClinicalDefinitionPackage package)
    {
        if (package.Profile != ClinicalDefinitionPackageProfile.SimplifiedDemoIntake ||
            package.Questionnaire.Id != session.QuestionnaireVersionId ||
            package.RuleDefinitions.DemoIntake is null)
        {
            throw new InvalidOperationException(
                "The session is not pinned to a usable simplified demo package.");
        }
    }
}

public sealed record ReplayPreTriageIntakeQuery(
    EntityId SessionId,
    IReadOnlyList<string> InitialAnswerCodes,
    PreTriageCallerMode CallerMode,
    string? AnonymousCapability);

public sealed record PreTriageIntakeReplayState(
    PreTriageSession Session,
    IReadOnlyCollection<TriageAnswer> Answers);

public interface IPreTriageIntakeReplayRepository
{
    Task<PreTriageIntakeReplayState?> LoadAsync(
        EntityId sessionId,
        CancellationToken cancellationToken = default);
}

public sealed class PreTriageAnonymousReplayCapabilityRequiredException : Exception
{
    public PreTriageAnonymousReplayCapabilityRequiredException()
        : base("Anonymous intake replay requires the originally issued capability.")
    {
    }
}
