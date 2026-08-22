using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;

namespace Beeexy.Tests.Unit.Triage;

public sealed class ProjectCompletedPreTriageEpisodeTests
{
    [Fact]
    public async Task CompletedPatientEpisode_ProjectsFrozenNeutralSummaryRepeatably()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
        var graph = CreateEligibleGraph(package, anonymousClaim: false);
        var provider = new FrozenDefinitionProvider(package);
        var repository = new FakeProjectionRepository(graph);
        var useCase = new ProjectCompletedPreTriageEpisode(
            provider,
            new CheckDemoQuestionnaireCompleteness(),
            repository);

        var first = await useCase.ExecuteAsync(graph.Episode.Id);
        var second = await useCase.ExecuteAsync(graph.Episode.Id);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(PreTriageHistoryProjection.SourceTypeCode, first.SourceType);
        Assert.Equal(graph.Episode.Id, first.SourceEpisodeId);
        Assert.Equal(graph.Episode.PatientProfileId, first.PatientProfileId);
        Assert.Equal(graph.Episode.CompletedAt, first.CompletedAt);
        Assert.Equal("HEADACHE", first.PrimarySymptom.Value);
        Assert.Equal("Headache", first.PrimarySymptomDisplay);
        Assert.Equal(2, first.DurationValue);
        Assert.Equal("DAYS", first.DurationUnit);
        Assert.Equal(7, first.Intensity);
        Assert.Equal(["FEVER"], first.AdditionalSymptoms);
        Assert.Equal(package.Questionnaire.QuestionnaireCode, first.QuestionnaireCode);
        Assert.Equal(package.Questionnaire.Version, first.QuestionnaireVersion);
        Assert.Equal(package.RuleSet.RuleSetCode, first.PackageCode);
        Assert.Equal(package.RuleSet.Version, first.PackageVersion);
        Assert.Equal(ClinicalContentStatus.NonClinicalDemo, first.ContentStatus);
        Assert.Equivalent(first, second, strict: true);
        Assert.Equal(2, repository.Reads);
        Assert.Equal(2, provider.FrozenReads);
        Assert.Equal(0, provider.ActiveReads);
    }

    [Fact]
    public async Task MissingEligibilityRecord_ProducesNoProjectionOrDefinitionLookup()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Fever);
        var provider = new FrozenDefinitionProvider(package);
        var useCase = new ProjectCompletedPreTriageEpisode(
            provider,
            new CheckDemoQuestionnaireCompleteness(),
            new FakeProjectionRepository(null));

        var result = await useCase.ExecuteAsync(EntityId.New());

        Assert.Null(result);
        Assert.Equal(0, provider.FrozenReads);
        Assert.Equal(0, provider.ActiveReads);
    }

    [Fact]
    public async Task FrozenVersionA_IsUsedWhenASeparateVersionBIsActive()
    {
        var frozen = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.AbdominalPain);
        var active = AbdominalPainProvisionalPackage.Create();
        Assert.Equal(frozen.Pathway, active.Pathway);
        Assert.NotEqual(frozen.Version, active.Version);
        var graph = CreateEligibleGraph(frozen, anonymousClaim: false);
        var provider = new FrozenDefinitionProvider(frozen, active);
        var useCase = new ProjectCompletedPreTriageEpisode(
            provider,
            new CheckDemoQuestionnaireCompleteness(),
            new FakeProjectionRepository(graph));

        var result = await useCase.ExecuteAsync(graph.Episode.Id);

        Assert.NotNull(result);
        Assert.Equal(frozen.Questionnaire.Version, result.QuestionnaireVersion);
        Assert.Equal(frozen.RuleSet.Version, result.PackageVersion);
        Assert.NotEqual(active.Version, result.QuestionnaireVersion);
        Assert.Equal(1, provider.FrozenReads);
        Assert.Equal(0, provider.ActiveReads);
    }

    [Fact]
    public async Task UrgencyBearingSourceGraph_IsRejectedBeforeAnyProjection()
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
        var provider = new FrozenDefinitionProvider(package);
        var useCase = new ProjectCompletedPreTriageEpisode(
            provider,
            new CheckDemoQuestionnaireCompleteness(),
            new FakeProjectionRepository(invalid));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(valid.Episode.Id));

        Assert.Equal(0, provider.FrozenReads);
    }

    [Fact]
    public void EligibilityRecord_RejectsAnonymousUnclaimedEpisodeAndFreezesClaimOwner()
    {
        var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Fever);
        var now = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
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

        Assert.Equal(episode.Id, record.SourceEpisodeId);
        Assert.Equal(patientId, record.PatientProfileId);
        Assert.Equal(episode.CompletedAt, record.CompletedAt);
        Assert.Equal(claimedAt, record.CreatedAt);
    }

    [Fact]
    public void ProjectionContract_ContainsNoClinicalAuthorityOrTransportFields()
    {
        var forbidden = new[]
        {
            "urgency", "disposition", "redflag", "diagnosis", "probability",
            "prescription", "treatment", "recommendation", "provider", "model",
            "prompt", "conversation", "capability", "token", "fhir"
        };
        var names = typeof(PreTriageHistoryProjection)
            .GetProperties()
            .Select(property => property.Name.Replace("_", string.Empty))
            .ToArray();

        Assert.All(names, name => Assert.DoesNotContain(
            forbidden,
            value => name.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    private static PreTriageHistoryProjectionGraph CreateEligibleGraph(
        ClinicalDefinitionPackage package,
        bool anonymousClaim)
    {
        var now = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
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

        var assessment = ClinicalAssessment.CreateNeutral(episode, episode.CompletedAt);
        var record = PreTriageHistoryProjectionRecord.Create(episode, createdAt);
        return new PreTriageHistoryProjectionGraph(record, session, episode, assessment);
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
        AddAnswer(session, package, "ADDITIONAL_SYMPTOMS", "{\"values\":[\"FEVER\"]}", now);
        session.ReportSymptom(
            SymptomText.Create(package.Pathway.Value),
            1,
            now.AddMinutes(1),
            "urn:beeexy:demo-symptom-code",
            package.Pathway.Value,
            "Headache",
            "BEEEXY_SIMPLIFIED_DEMO_PACKAGE",
            now.AddMinutes(1));
        session.ReportSymptom(
            SymptomText.Create("FEVER"),
            2,
            now.AddMinutes(1),
            "urn:beeexy:demo-symptom-code",
            "FEVER",
            "FEVER",
            "BEEEXY_SIMPLIFIED_DEMO_PACKAGE",
            now.AddMinutes(1));
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

    private sealed class FakeProjectionRepository(PreTriageHistoryProjectionGraph? graph)
        : IPreTriageHistoryProjectionRepository
    {
        public int Reads { get; private set; }

        public Task<PreTriageHistoryProjectionGraph?> GetAsync(
            EntityId sourceEpisodeId,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(
                graph?.Episode.Id == sourceEpisodeId ? graph : null);
        }
    }

    private sealed class FrozenDefinitionProvider(
        ClinicalDefinitionPackage package,
        ClinicalDefinitionPackage? activePackage = null)
        : IClinicalDefinitionProvider
    {
        public int ActiveReads { get; private set; }

        public int FrozenReads { get; private set; }

        public Task<ClinicalDefinitionPackage?> GetActiveDefinitionAsync(
            ClinicalPathwayCode pathway,
            CancellationToken cancellationToken = default)
        {
            ActiveReads++;
            return Task.FromResult(activePackage);
        }

        public Task<ClinicalDefinitionPackage?> GetDefinitionAsync(
            ClinicalPathwayCode pathway,
            DefinitionVersion version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ClinicalDefinitionPackage?>(null);

        public Task<ClinicalDefinitionPackage?> GetDefinitionByQuestionnaireIdAsync(
            EntityId questionnaireVersionId,
            CancellationToken cancellationToken = default)
        {
            FrozenReads++;
            return Task.FromResult<ClinicalDefinitionPackage?>(
                package.Questionnaire.Id == questionnaireVersionId ? package : null);
        }
    }
}
