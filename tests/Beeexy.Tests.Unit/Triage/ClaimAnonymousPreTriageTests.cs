using Beeexy.Application.Common;
using Beeexy.Application.Identity;
using Beeexy.Application.Patients;
using Beeexy.Application.Triage;
using Beeexy.Domain.Common;
using Beeexy.Domain.Triage;
using Beeexy.Infrastructure.Triage;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Triage;

public sealed class ClaimAnonymousPreTriageTests
{
    [Fact]
    public async Task CompletedAnonymousEpisode_ClaimsExistingGraphForPrimaryPatient()
    {
        var fixture = new Fixture();
        var episode = Assert.IsType<PreTriageEpisode>(fixture.Episode);
        var assessment = Assert.IsType<ClinicalAssessment>(fixture.Assessment);
        var episodeId = episode.Id;
        var assessmentId = assessment.Id;
        var completedAt = episode.CompletedAt;

        var result = await fixture.ClaimAsync();

        Assert.Equal(fixture.Profiles.PrimaryProfile.Id, result.PatientProfileId);
        Assert.Equal(episodeId, result.EpisodeId);
        Assert.Equal(assessmentId, assessment.Id);
        Assert.Equal(completedAt, episode.CompletedAt);
        Assert.Null(assessment.UrgencyCode);
        Assert.Empty(assessment.Findings);
        Assert.Equal(1, fixture.Repository.SaveCount);
        Assert.Single(fixture.Audit.Transitions);
    }

    [Fact]
    public async Task SamePrimaryPatientRepeat_IsIdempotentWithStableClaimTimestamp()
    {
        var fixture = new Fixture();
        var first = await fixture.ClaimAsync();
        fixture.Clock.Now = Fixture.Now.AddHours(30);

        var repeat = await fixture.ClaimAsync();

        Assert.Equal(first, repeat);
        Assert.Equal(first.ClaimedAt, fixture.Episode!.ClaimedAt);
        Assert.Equal(1, fixture.Repository.SaveCount);
        Assert.Single(fixture.Audit.Transitions);
    }

    [Fact]
    public async Task DifferentPrimaryPatientRepeat_ConflictsWithoutTransfer()
    {
        var fixture = new Fixture();
        await fixture.ClaimAsync();
        var episode = Assert.IsType<PreTriageEpisode>(fixture.Episode);
        var owner = episode.PatientProfileId;
        var claimedAt = episode.ClaimedAt;
        var other = new MyCircleListingTestFixture();

        await Assert.ThrowsAsync<PreTriageClaimConflictException>(() =>
            fixture.ClaimAsync(other.Resolver));

        Assert.Equal(owner, episode.PatientProfileId);
        Assert.Equal(claimedAt, episode.ClaimedAt);
        Assert.Equal(1, fixture.Repository.SaveCount);
        Assert.Single(fixture.Audit.Transitions);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task FirstClaim_UsesExactPersistedExpiryBoundary(
        int offsetMilliseconds,
        bool succeeds)
    {
        var fixture = new Fixture();
        fixture.Clock.Now = fixture.Session.ExpiresAt.AddMilliseconds(offsetMilliseconds);

        if (succeeds)
        {
            await fixture.ClaimAsync();
            Assert.Equal(fixture.Profiles.PrimaryProfile.Id,
                fixture.Episode!.PatientProfileId);
        }
        else
        {
            await Assert.ThrowsAsync<PreTriageSessionNotFoundException>(() =>
                fixture.ClaimAsync());
            var episode = Assert.IsType<PreTriageEpisode>(fixture.Episode);
            Assert.Null(episode.PatientProfileId);
            Assert.Null(episode.ClaimedAt);
        }
    }

    [Fact]
    public async Task MissingOrWrongCapability_IsUnauthorizedWithoutMutation()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<SessionAuthenticationException>(() =>
            fixture.ClaimAsync(capability: null));
        await Assert.ThrowsAsync<SessionAuthenticationException>(() =>
            fixture.ClaimAsync(capability: "wrong-capability"));

        Assert.Null(fixture.Episode!.PatientProfileId);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Audit.Transitions);
    }

    [Fact]
    public async Task ActiveSession_ConflictsAndDoesNotCreatePermanentState()
    {
        var fixture = new Fixture(completed: false);

        await Assert.ThrowsAsync<PreTriageSessionStateConflictException>(() =>
            fixture.ClaimAsync());

        Assert.Null(fixture.Repository.Graph.Episode);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Audit.Transitions);
    }

    private sealed class Fixture
    {
        public static readonly DateTimeOffset Now =
            new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

        private readonly CryptographicAnonymousPreTriageCapabilityService _capabilities = new();

        public Fixture(bool completed = true)
        {
            var package = SimplifiedDemoDefinitionPackages.Create(ClinicalPathways.Headache);
            var generated = _capabilities.Generate();
            Capability = generated.Value;
            Session = PreTriageSession.CreateAnonymous(
                package.Questionnaire.Id,
                generated.Hash,
                Now.AddHours(24),
                Now);

            if (completed)
            {
                var question = package.Questionnaire.Questions.Single(
                    value => value.Code == QuestionCode.Create("INTENSITY"));
                Session.RecordAnswer(
                    question,
                    "{\"value\":7}",
                    question.DisplayOrder,
                    Now.AddMinutes(1));
                Session.ReportSymptom(
                    SymptomText.Create("HEADACHE"),
                    1,
                    Now.AddMinutes(1),
                    terminologySystem: "urn:beeexy:demo-symptom-code",
                    terminologyCode: "HEADACHE",
                    terminologyDisplay: "Headache",
                    normalizationSource: "BEEEXY_SIMPLIFIED_DEMO_PACKAGE",
                    normalizedAt: Now.AddMinutes(1));
                Episode = PreTriageEpisode.CreateFrom(
                    Session,
                    package.RuleSet.Id,
                    Now.AddMinutes(2),
                    Session.ExpiresAt);
                Assessment = ClinicalAssessment.CreateNeutral(
                    Episode,
                    Now.AddMinutes(2));
            }

            Repository.Graph = new ClaimablePreTriageGraph(
                Session,
                Episode,
                Assessment);
        }

        public MutableClock Clock { get; } = new();

        public MyCircleListingTestFixture Profiles { get; } = new();

        public FakeClaimRepository Repository { get; } = new();

        public FakeClaimAuditLogger Audit { get; } = new();

        public PreTriageSession Session { get; }

        public PreTriageEpisode? Episode { get; }

        public ClinicalAssessment? Assessment { get; }

        public string Capability { get; }

        public Task<ClaimAnonymousPreTriageResult> ClaimAsync(
            CurrentAccountProfileResolver? resolver = null,
            string? capability = "use-valid")
        {
            var useCase = new ClaimAnonymousPreTriage(
                Clock,
                resolver ?? Profiles.Resolver,
                _capabilities,
                Repository,
                Audit);
            return useCase.ExecuteAsync(new ClaimAnonymousPreTriageCommand(
                Session.Id,
                capability == "use-valid" ? Capability : capability));
        }
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset Now { get; set; } = Fixture.Now.AddMinutes(3);

        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeClaimRepository : IPreTriageClaimRepository
    {
        public ClaimablePreTriageGraph Graph { get; set; } = null!;

        public int SaveCount { get; private set; }

        public Task<ClaimAnonymousPreTriageMutation?> ExecuteLockedAsync(
            EntityId sessionId,
            Func<ClaimablePreTriageGraph, ClaimAnonymousPreTriageMutation> mutation,
            CancellationToken cancellationToken = default)
        {
            if (Graph.Session.Id != sessionId)
            {
                return Task.FromResult<ClaimAnonymousPreTriageMutation?>(null);
            }

            var result = mutation(Graph);
            if (result.IsNewlyClaimed)
            {
                SaveCount++;
            }

            return Task.FromResult<ClaimAnonymousPreTriageMutation?>(result);
        }
    }

    private sealed class FakeClaimAuditLogger : IPreTriageClaimAuditLogger
    {
        public List<ClaimAnonymousPreTriageResult> Transitions { get; } = [];

        public void ClaimTransitioned(
            EntityId sessionId,
            EntityId episodeId,
            EntityId patientProfileId,
            DateTimeOffset claimedAt) => Transitions.Add(new ClaimAnonymousPreTriageResult(
            sessionId,
            episodeId,
            patientProfileId,
            claimedAt));
    }
}
