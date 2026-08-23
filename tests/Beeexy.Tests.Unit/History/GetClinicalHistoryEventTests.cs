using Beeexy.Application.History;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.History;

public sealed class GetClinicalHistoryEventTests
{
    [Fact]
    public async Task PrimaryPatientRetrievesTheExactPatientScopedEvent()
    {
        var fixture = new Fixture();

        var result = await fixture.GetAsync(fixture.Profiles.PrimaryProfile.Id);

        Assert.Same(fixture.Detail, result);
        Assert.Equal(fixture.Profiles.PrimaryProfile.Id, fixture.Repository.PatientId);
        Assert.Equal(fixture.Detail.Event.EventId, fixture.Repository.EventId);
    }

    [Fact]
    public async Task ActiveManagerRetrievesTheManagedPatientEvent()
    {
        var fixture = new Fixture();
        var patientId = EntityId.New();
        fixture.AuthorizationRepository.Set(patientId, true, EntityId.New());

        var result = await fixture.GetAsync(patientId);

        Assert.Same(fixture.Detail, result);
        Assert.Equal(patientId, fixture.Repository.PatientId);
    }

    [Fact]
    public async Task MissingAndUnauthorizedPatientsAreConcealedBeforeEventLookup()
    {
        var fixture = new Fixture();
        var missing = EntityId.New();
        var unauthorized = EntityId.New();
        fixture.AuthorizationRepository.Set(missing, false);
        fixture.AuthorizationRepository.Set(unauthorized, true);

        var missingFailure = await Assert.ThrowsAsync<PatientProfileNotFoundException>(
            () => fixture.GetAsync(missing));
        var unauthorizedFailure =
            await Assert.ThrowsAsync<PatientProfileNotFoundException>(
                () => fixture.GetAsync(unauthorized));

        Assert.Equal(missingFailure.Message, unauthorizedFailure.Message);
        Assert.Equal(0, fixture.Repository.CallCount);
    }

    [Fact]
    public async Task AbsentAndWrongPatientEventsUseTheSameConcealedFailure()
    {
        var fixture = new Fixture();
        fixture.Repository.Result = null;
        var managedPatient = EntityId.New();
        fixture.AuthorizationRepository.Set(managedPatient, true, EntityId.New());

        var absent = await Assert.ThrowsAsync<PatientProfileNotFoundException>(
            () => fixture.GetAsync(fixture.Profiles.PrimaryProfile.Id));
        var wrongPatient = await Assert.ThrowsAsync<PatientProfileNotFoundException>(
            () => fixture.GetAsync(managedPatient));

        Assert.Equal(absent.Message, wrongPatient.Message);
        Assert.Equal(2, fixture.Repository.CallCount);
    }

    private sealed class Fixture
    {
        private static readonly DateTimeOffset Now =
            new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);

        public Fixture()
        {
            var item = new ClinicalHistoryListItem(
                EntityId.New(),
                ClinicalHistoryEventType.CompletedPreTriage,
                Now,
                Now.AddSeconds(1),
                AuthoritativeClinicalSourceType.PreTriageEpisode,
                EntityId.New(),
                EntityId.New(),
                EntityId.New());
            Detail = new ClinicalHistoryEventDetail(
                item,
                new ClinicalHistorySourceDetail(
                    item.SourceType,
                    item.SourceId,
                    item.OccurredAt,
                    item.QuestionnaireVersionId,
                    item.ClinicalRuleSetVersionId),
                []);
            Repository.Result = Detail;
        }

        public MyCircleListingTestFixture Profiles { get; } = new();

        public FakeAuthorizationRepository AuthorizationRepository { get; } = new();

        public FakeEventRepository Repository { get; } = new();

        public ClinicalHistoryEventDetail Detail { get; }

        public Task<ClinicalHistoryEventDetail> GetAsync(EntityId patientId)
        {
            var authorizer = new AuthorizePatientAccess(
                new FakeClock(),
                Profiles.Resolver,
                AuthorizationRepository,
                Profiles.MyCircleAudit);
            return new GetClinicalHistoryEvent(authorizer, Repository).ExecuteAsync(
                patientId,
                Detail.Event.EventId);
        }
    }

    private sealed class FakeAuthorizationRepository
        : IPatientAccessAuthorizationRepository
    {
        private readonly Dictionary<EntityId, PatientAccessAuthorizationLookup> _lookups = [];

        public void Set(
            EntityId targetProfileId,
            bool targetExists,
            EntityId? relationshipId = null) =>
            _lookups[targetProfileId] = new PatientAccessAuthorizationLookup(
                targetExists,
                relationshipId);

        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_lookups[targetProfileId]);
    }

    private sealed class FakeEventRepository : IClinicalHistoryEventReadRepository
    {
        public ClinicalHistoryEventDetail? Result { get; set; }

        public int CallCount { get; private set; }

        public EntityId? PatientId { get; private set; }

        public EntityId? EventId { get; private set; }

        public Task<ClinicalHistoryEventDetail?> GetAsync(
            EntityId patientProfileId,
            EntityId eventId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            PatientId = patientProfileId;
            EventId = eventId;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow =>
            new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);
    }
}
