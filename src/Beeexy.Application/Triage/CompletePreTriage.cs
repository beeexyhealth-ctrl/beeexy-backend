using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;

namespace Beeexy.Application.Triage;

public sealed class CompletePreTriage(
    IClock clock,
    AuthorizePatientAccess authorizePatientAccess,
    IAnonymousPreTriageCapabilityService capabilityService,
    IClinicalDefinitionProvider definitionProvider,
    CheckDemoQuestionnaireCompleteness completeness,
    NeutralClinicalAssessmentFactory assessmentFactory,
    IPreTriageCompletionRepository repository,
    IPreTriageCompletionAuditLogger auditLogger)
{
    public async Task<CompletePreTriageResult> ExecuteAsync(
        CompletePreTriageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = await repository.ExecuteLockedAsync(
            command.SessionId,
            async (session, completedGraph) =>
            {
                await AuthorizeAsync(session, command.CallerMode,
                    command.AnonymousCapability, cancellationToken);
                var now = ToPostgreSqlPrecision(clock.UtcNow);

                if (session.Status == PreTriageSessionStatus.Completed)
                {
                    if (completedGraph is null ||
                        completedGraph.Assessment.UrgencyCode is not null ||
                        completedGraph.Assessment.Findings.Count != 0)
                    {
                        throw new InvalidOperationException(
                            "The completed neutral pre-triage graph is inconsistent.");
                    }

                    EnsureResultAvailable(session, completedGraph.Episode, now);
                    var existingPackage = await LoadPinnedPackageAsync(
                        session, completedGraph.Episode, cancellationToken);
                    var existingSummary = completeness.CheckPermanent(
                        completedGraph.Episode, existingPackage);
                    return new PreTriageCompletionMutation<CompletePreTriageResult>(
                        new CompletePreTriageResult(
                            BuildCanonical(session, completedGraph.Episode,
                                existingPackage, existingSummary),
                            IsNewlyCompleted: false));
                }

                if (now >= session.ExpiresAt)
                {
                    throw new PreTriageSessionNotFoundException();
                }

                var package = await LoadPinnedPackageAsync(session, null, cancellationToken);
                var summary = completeness.CheckTemporary(session, package);
                MaterializeControlledSymptoms(session, package, summary, now);
                var episode = PreTriageEpisode.CreateFrom(
                    session,
                    package.RuleSet.Id,
                    now,
                    session.IsAnonymous ? session.ExpiresAt : null);
                var assessment = assessmentFactory.Create(episode, now);
                return new PreTriageCompletionMutation<CompletePreTriageResult>(
                    new CompletePreTriageResult(
                        BuildCanonical(session, episode, package, summary),
                        IsNewlyCompleted: true),
                    episode,
                    assessment);
            },
            cancellationToken) ?? throw new PreTriageSessionNotFoundException();

        auditLogger.CompletionProcessed(
            command.SessionId,
            command.CallerMode,
            result.IsNewlyCompleted,
            result.Result.CompletedAt);
        return result;
    }

    private async Task<ClinicalDefinitionPackage> LoadPinnedPackageAsync(
        PreTriageSession session,
        PreTriageEpisode? episode,
        CancellationToken cancellationToken)
    {
        var package = await definitionProvider.GetDefinitionByQuestionnaireIdAsync(
            session.QuestionnaireVersionId,
            cancellationToken) ?? throw new InvalidOperationException(
                "The session's pinned questionnaire package is unavailable.");
        if (package.Profile != ClinicalDefinitionPackageProfile.SimplifiedDemoIntake ||
            package.RuleDefinitions.DemoIntake is null ||
            package.Questionnaire.Id != session.QuestionnaireVersionId ||
            (episode is not null &&
                (episode.QuestionnaireVersionId != package.Questionnaire.Id ||
                 episode.ClinicalRuleSetVersionId != package.RuleSet.Id)))
        {
            throw InvalidCompletion("pre_triage.definition_inconsistent",
                "The pinned demo questionnaire is inconsistent.");
        }

        return package;
    }

    private async Task AuthorizeAsync(
        PreTriageSession session,
        PreTriageCallerMode callerMode,
        string? anonymousCapability,
        CancellationToken cancellationToken)
    {
        if (callerMode == PreTriageCallerMode.Anonymous)
        {
            if (!session.IsAnonymous || session.AnonymousCapabilityHash is null ||
                !capabilityService.Verify(anonymousCapability, session.AnonymousCapabilityHash))
            {
                throw new SessionAuthenticationException();
            }

            return;
        }

        if (session.PatientProfileId is null)
        {
            throw new PreTriageSessionNotFoundException();
        }

        var authorization = await authorizePatientAccess.ExecuteForPatientUpdateAsync(
            session.PatientProfileId.Value,
            cancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new PreTriageSessionNotFoundException();
        }
    }

    private static void EnsureResultAvailable(
        PreTriageSession session,
        PreTriageEpisode episode,
        DateTimeOffset now)
    {
        if (session.IsAnonymous &&
            (!episode.AnonymousExpiresAt.HasValue || now >= episode.AnonymousExpiresAt.Value))
        {
            throw new PreTriageSessionNotFoundException();
        }
    }

    private static void MaterializeControlledSymptoms(
        PreTriageSession session,
        ClinicalDefinitionPackage package,
        DemoIntakeSummaryData summary,
        DateTimeOffset now)
    {
        var symptoms = new[] { package.Pathway.Value }.Concat(summary.AdditionalSymptoms);
        var sequence = 1;
        foreach (var code in symptoms)
        {
            session.ReportSymptom(
                SymptomText.Create(code),
                sequence++,
                now,
                terminologySystem: "urn:beeexy:demo-symptom-code",
                terminologyCode: code,
                terminologyDisplay: code == package.Pathway.Value
                    ? summary.PrimarySymptomDisplay
                    : code,
                normalizationSource: "BEEEXY_SIMPLIFIED_DEMO_PACKAGE",
                normalizedAt: now);
        }
    }

    internal static NeutralPreTriageResult BuildCanonical(
        PreTriageSession session,
        PreTriageEpisode episode,
        ClinicalDefinitionPackage package,
        DemoIntakeSummaryData summary) => new(
            session.Id,
            episode.Id,
            package.Pathway,
            summary.PrimarySymptomDisplay,
            summary.DurationValue,
            summary.DurationUnit,
            summary.Intensity,
            summary.AdditionalSymptoms,
            episode.CompletedAt,
            package.Questionnaire.QuestionnaireCode,
            package.Questionnaire.Version,
            package.RuleSet.RuleSetCode,
            package.RuleSet.Version,
            package.ContentStatus);

    private static RequestValidationException InvalidCompletion(string code, string message) =>
        new(code, message);

    private static DateTimeOffset ToPostgreSqlPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);
}

public sealed class GetPreTriageResult(
    IClock clock,
    AuthorizePatientAccess authorizePatientAccess,
    IAnonymousPreTriageCapabilityService capabilityService,
    IClinicalDefinitionProvider definitionProvider,
    CheckDemoQuestionnaireCompleteness completeness,
    IPreTriageCompletionRepository repository,
    IPreTriageCompletionAuditLogger auditLogger)
{
    public async Task<NeutralPreTriageResult> ExecuteAsync(
        GetPreTriageResultQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var graph = await repository.GetAsync(query.SessionId, cancellationToken) ??
            throw new PreTriageSessionNotFoundException();
        await AuthorizeAsync(graph, query, cancellationToken);

        if (graph.Session.Status != PreTriageSessionStatus.Completed)
        {
            if (clock.UtcNow >= graph.Session.ExpiresAt)
            {
                throw new PreTriageSessionNotFoundException();
            }

            throw new PreTriageSessionStateConflictException(
                "The pre-triage result is not available until completion.");
        }

        if (graph.Episode is null || graph.Assessment is null ||
            graph.Assessment.UrgencyCode is not null || graph.Assessment.Findings.Count != 0)
        {
            throw new InvalidOperationException(
                "The completed neutral pre-triage graph is inconsistent.");
        }

        if (graph.Session.IsAnonymous &&
            (query.CallerMode == PreTriageCallerMode.Anonymous ||
             !graph.Episode.IsClaimed) &&
            (!graph.Episode.AnonymousExpiresAt.HasValue ||
             clock.UtcNow >= graph.Episode.AnonymousExpiresAt.Value))
        {
            throw new PreTriageSessionNotFoundException();
        }

        var package = await definitionProvider.GetDefinitionByQuestionnaireIdAsync(
            graph.Session.QuestionnaireVersionId,
            cancellationToken) ?? throw new InvalidOperationException(
                "The completed questionnaire package is unavailable.");
        if (package.Profile != ClinicalDefinitionPackageProfile.SimplifiedDemoIntake ||
            package.Questionnaire.Id != graph.Episode.QuestionnaireVersionId ||
            package.RuleSet.Id != graph.Episode.ClinicalRuleSetVersionId)
        {
            throw new InvalidOperationException(
                "The completed questionnaire provenance is inconsistent.");
        }

        var summary = completeness.CheckPermanent(graph.Episode, package);
        var result = CompletePreTriage.BuildCanonical(
            graph.Session, graph.Episode, package, summary);
        auditLogger.ResultRetrieved(query.SessionId, query.CallerMode, result.CompletedAt);
        return result;
    }

    private async Task AuthorizeAsync(
        StoredPreTriageGraph graph,
        GetPreTriageResultQuery query,
        CancellationToken cancellationToken)
    {
        var session = graph.Session;
        if (query.CallerMode == PreTriageCallerMode.Anonymous)
        {
            if (!session.IsAnonymous || session.AnonymousCapabilityHash is null ||
                !capabilityService.Verify(
                    query.AnonymousCapability, session.AnonymousCapabilityHash))
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
}

public sealed class CheckDemoQuestionnaireCompleteness
{
    public DemoIntakeSummaryData CheckTemporary(
        PreTriageSession session,
        ClinicalDefinitionPackage package)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.ReportedSymptoms.Count != 0)
        {
            throw Incomplete("The temporary symptom state is inconsistent.");
        }

        return CheckAnswers(session.QuestionnaireVersionId, session.Answers, package);
    }

    public DemoIntakeSummaryData CheckPermanent(
        PreTriageEpisode episode,
        ClinicalDefinitionPackage package)
    {
        ArgumentNullException.ThrowIfNull(episode);
        var summary = CheckAnswers(episode.QuestionnaireVersionId, episode.Answers, package);
        var expected = new[] { package.Pathway.Value }.Concat(summary.AdditionalSymptoms).ToArray();
        var actual = episode.ReportedSymptoms
            .OrderBy(symptom => symptom.Sequence)
            .Select(symptom => symptom.TerminologyCode)
            .ToArray();
        if (actual.Length != expected.Length ||
            actual.Where(value => value is not null).Cast<string>()
                .SequenceEqual(expected, StringComparer.Ordinal) is false)
        {
            throw new InvalidOperationException(
                "The permanent controlled symptom summary is inconsistent.");
        }

        return summary;
    }

    private static DemoIntakeSummaryData CheckAnswers(
        EntityId questionnaireVersionId,
        IReadOnlyCollection<TriageAnswer> answers,
        ClinicalDefinitionPackage package)
    {
        try
        {
            var demo = package.RuleDefinitions.DemoIntake;
            if (package.Profile != ClinicalDefinitionPackageProfile.SimplifiedDemoIntake ||
                demo is null || package.Questionnaire.Id != questionnaireVersionId ||
                package.ContentStatus != ClinicalContentStatus.NonClinicalDemo)
            {
                throw Incomplete("The pinned definition is not a valid demo package.");
            }

            var questionEntities = package.Questionnaire.Questions.ToDictionary(q => q.Id);
            var definitions = package.Questions.ToDictionary(q => q.Code);
            var required = demo.RequiredAnswerQuestionCodes.ToHashSet();
            if (answers.Count != required.Count)
            {
                throw Incomplete("The minimum demo questionnaire is incomplete.");
            }

            var values = new Dictionary<QuestionCode, ClinicalAiCandidateValue>();
            foreach (var answer in answers)
            {
                if (answer.QuestionnaireVersionId != questionnaireVersionId ||
                    !questionEntities.TryGetValue(answer.QuestionId, out var question) ||
                    !required.Contains(question.Code) || !values.TryAdd(
                        question.Code,
                        DemoTriageAnswerCodec.Decode(answer.AnswerJson, question.Code, package)))
                {
                    throw Incomplete("The stored demo answers are inconsistent.");
                }
            }

            foreach (var code in required)
            {
                if (!values.TryGetValue(code, out var value) ||
                    ClinicalAnswerValueValidator.Validate(value, definitions[code], package)
                        .HasValue)
                {
                    throw Incomplete("The minimum demo questionnaire is incomplete or invalid.");
                }
            }

            var primaryQuestion = definitions[demo.PrimarySymptomQuestionCode];
            if (primaryQuestion.Answer.AllowedValues is not { Count: 1 } primaryAllowed ||
                !string.Equals(primaryAllowed[0], package.Pathway.Value,
                    StringComparison.Ordinal))
            {
                throw Incomplete("The pinned primary symptom is inconsistent.");
            }

            var duration = (ClinicalAiDurationValue)values[demo.DurationQuestionCode];
            var intensity = (ClinicalAiIntegerValue)values[demo.IntensityQuestionCode];
            var additional = (ClinicalAiMultipleChoiceValue)
                values[demo.AdditionalSymptomsQuestionCode];
            if (additional.Values.Any(value =>
                    string.Equals(value, package.Pathway.Value, StringComparison.Ordinal)) ||
                additional.Values.Any(value =>
                    !demo.ApplicableAdditionalSymptoms.Contains(value,
                        StringComparer.Ordinal)))
            {
                throw Incomplete("An additional symptom is not applicable to the pathway.");
            }

            var order = demo.ApplicableAdditionalSymptoms
                .Select((code, index) => (code, index))
                .ToDictionary(item => item.code, item => item.index, StringComparer.Ordinal);
            return new DemoIntakeSummaryData(
                demo.PrimarySymptomDisplayLabel,
                duration.Value,
                DurationUnitCode(duration.Unit),
                intensity.Value,
                additional.Values.OrderBy(value => order[value]).ToArray());
        }
        catch (RequestValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            ArgumentException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            throw Incomplete("The stored demo questionnaire state is invalid.");
        }
    }

    private static string DurationUnitCode(ClinicalDurationUnit unit) => unit switch
    {
        ClinicalDurationUnit.Minutes => "MINUTES",
        ClinicalDurationUnit.Hours => "HOURS",
        ClinicalDurationUnit.Days => "DAYS",
        ClinicalDurationUnit.Weeks => "WEEKS",
        ClinicalDurationUnit.Months => "MONTHS",
        _ => throw new InvalidOperationException("The stored duration unit is invalid.")
    };

    private static RequestValidationException Incomplete(string message) =>
        new("pre_triage.completion_incomplete", message);
}

public sealed class NeutralClinicalAssessmentFactory
{
    public ClinicalAssessment Create(PreTriageEpisode episode, DateTimeOffset createdAt) =>
        ClinicalAssessment.CreateNeutral(episode, createdAt);
}

public sealed record CompletePreTriageCommand(
    EntityId SessionId,
    PreTriageCallerMode CallerMode,
    string? AnonymousCapability);

public sealed record GetPreTriageResultQuery(
    EntityId SessionId,
    PreTriageCallerMode CallerMode,
    string? AnonymousCapability);

public sealed record DemoIntakeSummaryData(
    string PrimarySymptomDisplay,
    decimal DurationValue,
    string DurationUnit,
    int Intensity,
    IReadOnlyList<string> AdditionalSymptoms);

public sealed record NeutralPreTriageResult(
    EntityId SessionId,
    EntityId EpisodeId,
    ClinicalPathwayCode PrimarySymptom,
    string PrimarySymptomDisplay,
    decimal DurationValue,
    string DurationUnit,
    int Intensity,
    IReadOnlyList<string> AdditionalSymptoms,
    DateTimeOffset CompletedAt,
    QuestionnaireCode QuestionnaireCode,
    DefinitionVersion QuestionnaireVersion,
    RuleSetCode PackageCode,
    DefinitionVersion PackageVersion,
    ClinicalContentStatus ContentStatus);

public sealed record CompletePreTriageResult(
    NeutralPreTriageResult Result,
    bool IsNewlyCompleted);

public sealed record CompletedPreTriageGraph(
    PreTriageEpisode Episode,
    ClinicalAssessment Assessment);

public sealed record StoredPreTriageGraph(
    PreTriageSession Session,
    PreTriageEpisode? Episode,
    ClinicalAssessment? Assessment);

public sealed record PreTriageCompletionMutation<TResult>(
    TResult Result,
    PreTriageEpisode? NewEpisode = null,
    ClinicalAssessment? NewAssessment = null)
    where TResult : class;

public interface IPreTriageCompletionRepository
{
    Task<TResult?> ExecuteLockedAsync<TResult>(
        EntityId sessionId,
        Func<PreTriageSession, CompletedPreTriageGraph?,
            Task<PreTriageCompletionMutation<TResult>>> mutation,
        CancellationToken cancellationToken = default)
        where TResult : class;

    Task<StoredPreTriageGraph?> GetAsync(
        EntityId sessionId,
        CancellationToken cancellationToken = default);
}

public interface IPreTriageCompletionAuditLogger
{
    void CompletionProcessed(
        EntityId sessionId,
        PreTriageCallerMode callerMode,
        bool newlyCompleted,
        DateTimeOffset completedAt);

    void ResultRetrieved(
        EntityId sessionId,
        PreTriageCallerMode callerMode,
        DateTimeOffset completedAt);
}
