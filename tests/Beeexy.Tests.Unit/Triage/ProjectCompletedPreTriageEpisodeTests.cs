using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Triage;

public sealed class ProjectCompletedPreTriageEpisodeTests
{
    [Fact]
    public async Task CompletedPatientEpisode_ProjectsOneStableAuthoritativeReference()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var graph = CreateEligibleGraph(package, anonymousClaim: false);
        var recordedAt = graph.Episode.CompletedAt.AddMinutes(5);
        var repository = new FakeProjectionRepository(graph);
        var useCase = new ProjectCompletedPreTriageEpisode(
            new FixedClock(recordedAt),
            repository);

        var first = await useCase.ExecuteAsync(graph.Episode.Id);
        var second = await useCase.ExecuteAsync(graph.Episode.Id);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(first.IsNewlyProjected);
        Assert.False(second.IsNewlyProjected);
        Assert.Equal(first.Event.Id, second.Event.Id);
        Assert.Equal(graph.Episode.Id, first.Event.SourceId);
        Assert.Equal(graph.Episode.PatientProfileId, first.Event.PatientProfileId);
        Assert.Equal(
            ClinicalHistoryEventType.CompletedPreTriage,
            first.Event.EventType);
        Assert.Equal(
            AuthoritativeClinicalSourceType.PreTriageEpisode,
            first.Event.SourceType);
        Assert.Equal(graph.Episode.CompletedAt, first.Event.OccurredAt);
        Assert.Equal(recordedAt, first.Event.RecordedAt);
        Assert.Equal(graph.Episode.QuestionnaireVersionId,
            first.Event.SourceQuestionnaireVersionId);
        Assert.Equal(graph.Episode.ClinicalRuleSetVersionId,
            first.Event.SourceClinicalRuleSetVersionId);
        Assert.Equal(2, repository.Deliveries);
    }

    [Fact]
    public async Task MissingEligibilityRecord_ProducesNoHistoryEvent()
    {
        var repository = new FakeProjectionRepository(null);
        var useCase = new ProjectCompletedPreTriageEpisode(
            new FixedClock(Utc(18)),
            repository);

        var result = await useCase.ExecuteAsync(EntityId.New());

        Assert.Null(result);
        Assert.Null(repository.StoredEvent);
    }

    [Fact]
    public async Task FrozenEpisodeVersions_AreUsedWithoutDefinitionLookupOrReinterpretation()
    {
        var frozen = SimplifiedDemoDefinitionPackages.Create(
            ClinicalPathways.AbdominalPain);
        var currentlyActive = AbdominalPainProvisionalPackage.Create();
        Assert.NotEqual(frozen.Version, currentlyActive.Version);
        var graph = CreateEligibleGraph(frozen, anonymousClaim: false);
        var useCase = new ProjectCompletedPreTriageEpisode(
            new FixedClock(Utc(18)),
            new FakeProjectionRepository(graph));

        var result = await useCase.ExecuteAsync(graph.Episode.Id);

        Assert.NotNull(result);
        Assert.Equal(frozen.Questionnaire.Id,
            result.Event.SourceQuestionnaireVersionId);
        Assert.Equal(frozen.RuleSet.Id,
            result.Event.SourceClinicalRuleSetVersionId);
        Assert.NotEqual(currentlyActive.Questionnaire.Id,
            result.Event.SourceQuestionnaireVersionId);
        Assert.Equal(
            [typeof(IClock), typeof(IPreTriageHistoryProjectionRepository)],
            typeof(ProjectCompletedPreTriageEpisode).GetConstructors().Single()
                .GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task UrgencyBearingSourceGraph_IsRejectedWithoutCreatingHistory()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var valid = CreateEligibleGraph(package, anonymousClaim: false);
        var invalid = valid with
        {
            Assessment = ClinicalAssessment.Create(
                valid.Episode,
                UrgencyCode.Create("HIGH"),
                valid.Episode.CompletedAt)
        };
        var repository = new FakeProjectionRepository(invalid);
        var useCase = new ProjectCompletedPreTriageEpisode(
            new FixedClock(Utc(18)),
            repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(valid.Episode.Id));

        Assert.Null(repository.StoredEvent);
    }

    [Fact]
    public void EligibilityRecord_RejectsAnonymousUnclaimedEpisodeAndFreezesClaimOwner()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Fever);
        var now = Utc(12);
        var session = CreateCompletedSession(package, null, now);
        var episode = PreTriageEpisode.CreateFrom(
            session,
            package.RuleSet.Id,
            now.AddMinutes(2),
            session.ExpiresAt);

        Assert.Throws<InvalidOperationException>(() =>
            PreTriageHistoryProjectionRecord.Create(episode, episode.CompletedAt));

        var patientId = EntityId.New();
        var claimedAt = now.AddMinutes(3);
        episode.Claim(patientId, claimedAt);
        var record = PreTriageHistoryProjectionRecord.Create(episode, claimedAt);

        Assert.Equal(patientId, record.PatientProfileId);
        Assert.Equal(claimedAt, record.CreatedAt);
    }

    [Fact]
    public async Task ProjectionAcceptsNoCallerSuppliedPatientOverride()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Fever);
        var graph = CreateEligibleGraph(package, anonymousClaim: false);
        var useCase = new ProjectCompletedPreTriageEpisode(
            new FixedClock(Utc(18)),
            new FakeProjectionRepository(graph));

        var result = await useCase.ExecuteAsync(graph.Episode.Id);

        Assert.NotNull(result);
        Assert.Equal(graph.Episode.PatientProfileId, result.Event.PatientProfileId);
        var executeParameters = typeof(ProjectCompletedPreTriageEpisode)
            .GetMethod(nameof(ProjectCompletedPreTriageEpisode.ExecuteAsync))!
            .GetParameters();
        Assert.DoesNotContain(executeParameters, parameter =>
            parameter.Name!.Contains("patient", StringComparison.OrdinalIgnoreCase));
    }

    private static PreTriageHistoryProjectionGraph CreateEligibleGraph(
        ClinicalDefinitionPackage package,
        bool anonymousClaim)
    {
        var now = Utc(12);
        var patientId = EntityId.New();
        var session = CreateCompletedSession(
            package,
            anonymousClaim ? null : patientId,
            now);
        var episode = PreTriageEpisode.CreateFrom(
            session,
            package.RuleSet.Id,
            now.AddMinutes(2),
            anonymousClaim ? session.ExpiresAt : null);
        var createdAt = episode.CompletedAt;
        if (anonymousClaim)
        {
            createdAt = now.AddMinutes(3);
            episode.Claim(patientId, createdAt);
        }

        return new PreTriageHistoryProjectionGraph(
            PreTriageHistoryProjectionRecord.Create(episode, createdAt),
            session,
            episode,
            ClinicalAssessment.CreateNeutral(episode, episode.CompletedAt));
    }

    private static PreTriageSession CreateCompletedSession(
        ClinicalDefinitionPackage package,
        EntityId? patientId,
        DateTimeOffset now)
    {
        var session = patientId.HasValue
            ? PreTriageSession.CreateForPatient(
                patientId.Value,
                package.Questionnaire.Id,
                now.AddHours(24),
                now)
            : PreTriageSession.CreateAnonymous(
                package.Questionnaire.Id,
                AnonymousCapabilityHash.FromHash(new string('f', 64)),
                now.AddHours(24),
                now);
        AddAnswer(session, package, "DURATION", "{\"value\":2,\"unit\":\"DAYS\"}", now);
        AddAnswer(session, package, "INTENSITY", "{\"value\":7}", now);
        AddAnswer(
            session,
            package,
            "ADDITIONAL_SYMPTOMS",
            package.Pathway == ClinicalPathways.Fever
                ? "{\"values\":[\"NAUSEA\"]}"
                : "{\"values\":[\"FEVER\"]}",
            now);
        return session;
    }

    private static void AddAnswer(
        PreTriageSession session,
        ClinicalDefinitionPackage package,
        string code,
        string json,
        DateTimeOffset now)
    {
        var question = package.Questionnaire.Questions.Single(
            value => value.Code == QuestionCode.Create(code));
        session.RecordAnswer(question, json, question.DisplayOrder, now.AddMinutes(1));
    }

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 8, 23, hour, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FakeProjectionRepository(PreTriageHistoryProjectionGraph? graph)
        : IPreTriageHistoryProjectionRepository
    {
        public int Deliveries { get; private set; }

        public ClinicalHistoryEvent? StoredEvent { get; private set; }

        public Task<PreTriageHistoryProjectionOutcome?> ProjectAsync(
            EntityId sourceEpisodeId,
            Func<PreTriageHistoryProjectionGraph, ClinicalHistoryEvent> createEvent,
            CancellationToken cancellationToken = default)
        {
            Deliveries++;
            if (StoredEvent is not null)
            {
                return Task.FromResult<PreTriageHistoryProjectionOutcome?>(new(
                    StoredEvent,
                    IsNewlyProjected: false));
            }

            if (graph?.Episode.Id != sourceEpisodeId)
            {
                return Task.FromResult<PreTriageHistoryProjectionOutcome?>(null);
            }

            StoredEvent = createEvent(graph);
            return Task.FromResult<PreTriageHistoryProjectionOutcome?>(new(
                StoredEvent,
                IsNewlyProjected: true));
        }
    }
}
