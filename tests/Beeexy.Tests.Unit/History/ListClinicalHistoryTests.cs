using Beeexy.Application.Common;
using Beeexy.Application.History;
using Beeexy.Application.Patients;
using Beeexy.Domain.Common;
using Beeexy.Domain.History;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.History;

public sealed class ListClinicalHistoryTests
{
    [Fact]
    public async Task PrimaryPatient_DefaultPageUsesBoundedLookaheadAndReturnsCursor()
    {
        var fixture = new Fixture();
        fixture.ReadRepository.Results = Enumerable.Range(0, 21)
            .Select(fixture.CreateItem)
            .ToArray();

        var result = await fixture.ListAsync(
            fixture.Profiles.PrimaryProfile.Id);

        Assert.Equal(20, result.Items.Count);
        Assert.NotNull(result.NextCursor);
        Assert.Equal(21, fixture.ReadRepository.Take);
        Assert.Null(fixture.ReadRepository.After);
        Assert.Null(fixture.ReadRepository.EventType);
    }

    [Fact]
    public async Task CursorResumesAfterLastReturnedIdentity()
    {
        var fixture = new Fixture();
        fixture.ReadRepository.Results = Enumerable.Range(0, 3)
            .Select(fixture.CreateItem)
            .ToArray();
        var first = await fixture.ListAsync(
            fixture.Profiles.PrimaryProfile.Id,
            pageSize: 2);
        fixture.ReadRepository.Results = [];

        var second = await fixture.ListAsync(
            fixture.Profiles.PrimaryProfile.Id,
            first.NextCursor,
            pageSize: 2);

        Assert.Empty(second.Items);
        Assert.Null(second.NextCursor);
        Assert.NotNull(fixture.ReadRepository.After);
        Assert.Equal(first.Items[^1].EventId, fixture.ReadRepository.After.EventId);
        Assert.Equal(first.Items[^1].OccurredAt, fixture.ReadRepository.After.OccurredAt);
        Assert.Equal(fixture.Profiles.PrimaryProfile.Id,
            fixture.ReadRepository.After.PatientProfileId);
    }

    [Fact]
    public async Task CompletedPreTriageFilterIsValidatedAndBoundIntoCursor()
    {
        var fixture = new Fixture();
        fixture.ReadRepository.Results = Enumerable.Range(0, 2)
            .Select(fixture.CreateItem)
            .ToArray();
        var first = await fixture.ListAsync(
            fixture.Profiles.PrimaryProfile.Id,
            pageSize: 1,
            eventType: ClinicalHistoryEventTypes.CompletedPreTriage);
        fixture.ReadRepository.Results = [];

        await fixture.ListAsync(
            fixture.Profiles.PrimaryProfile.Id,
            first.NextCursor,
            pageSize: 1,
            eventType: ClinicalHistoryEventTypes.CompletedPreTriage);

        Assert.Equal(
            ClinicalHistoryEventType.CompletedPreTriage,
            fixture.ReadRepository.EventType);
        Assert.Equal(
            ClinicalHistoryEventType.CompletedPreTriage,
            fixture.ReadRepository.After!.EventType);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("completed_pre_triage")]
    public async Task UnsupportedEventTypeReturnsSafeValidationFailure(string eventType)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.ListAsync(fixture.Profiles.PrimaryProfile.Id, eventType: eventType));

        Assert.Equal("clinical_history.event_type_invalid", exception.Code);
        Assert.Equal(0, fixture.ReadRepository.CallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-1)]
    public async Task OutOfRangePageSizeReturnsSafeValidationFailure(int pageSize)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.ListAsync(fixture.Profiles.PrimaryProfile.Id, pageSize: pageSize));

        Assert.Equal("clinical_history.page_size_invalid", exception.Code);
        Assert.Equal(0, fixture.ReadRepository.CallCount);
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("====")]
    [InlineData("")]
    public async Task MalformedCursorReturnsSafeValidationFailure(string cursor)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.ListAsync(fixture.Profiles.PrimaryProfile.Id, cursor));

        Assert.Equal("clinical_history.cursor_invalid", exception.Code);
        Assert.Equal(0, fixture.ReadRepository.CallCount);
    }

    [Fact]
    public async Task CursorCannotBeReusedForAnotherPatientOrFilter()
    {
        var fixture = new Fixture();
        fixture.ReadRepository.Results = Enumerable.Range(0, 2)
            .Select(fixture.CreateItem)
            .ToArray();
        var first = await fixture.ListAsync(
            fixture.Profiles.PrimaryProfile.Id,
            pageSize: 1);
        var managed = EntityId.New();
        fixture.AuthorizeManaged(managed);

        var otherPatient = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.ListAsync(managed, first.NextCursor, pageSize: 1));
        var otherFilter = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.ListAsync(
                fixture.Profiles.PrimaryProfile.Id,
                first.NextCursor,
                pageSize: 1,
                eventType: ClinicalHistoryEventTypes.CompletedPreTriage));

        Assert.Equal("clinical_history.cursor_invalid", otherPatient.Code);
        Assert.Equal("clinical_history.cursor_invalid", otherFilter.Code);
        Assert.Equal(1, fixture.ReadRepository.CallCount);
    }

    [Fact]
    public async Task WellFormedCursorWhoseBoundaryDoesNotExistIsRejected()
    {
        var fixture = new Fixture();
        fixture.ReadRepository.Results = Enumerable.Range(0, 2)
            .Select(fixture.CreateItem)
            .ToArray();
        var first = await fixture.ListAsync(
            fixture.Profiles.PrimaryProfile.Id,
            pageSize: 1);
        fixture.ReadRepository.CursorExists = false;

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.ListAsync(
                fixture.Profiles.PrimaryProfile.Id,
                first.NextCursor,
                pageSize: 1));

        Assert.Equal("clinical_history.cursor_invalid", exception.Code);
        Assert.Equal(1, fixture.ReadRepository.CallCount);
        Assert.Equal(1, fixture.ReadRepository.CursorCheckCount);
    }

    [Fact]
    public async Task UnauthorizedAndMissingTargetsAreConcealedBeforeQueryValidationOrRead()
    {
        var fixture = new Fixture();
        var missing = EntityId.New();
        var unauthorized = EntityId.New();
        fixture.AuthorizationRepository.Set(missing, targetExists: false);
        fixture.AuthorizationRepository.Set(unauthorized, targetExists: true);

        var missingException = await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.ListAsync(missing, "bad cursor", eventType: "bad filter"));
        var unauthorizedException = await Assert.ThrowsAsync<PatientProfileNotFoundException>(() =>
            fixture.ListAsync(unauthorized, "bad cursor", eventType: "bad filter"));

        Assert.Equal(missingException.Message, unauthorizedException.Message);
        Assert.Equal(0, fixture.ReadRepository.CallCount);
    }

    private sealed class Fixture
    {
        private static readonly DateTimeOffset Now =
            new(2026, 8, 23, 14, 0, 0, TimeSpan.Zero);

        public MyCircleListingTestFixture Profiles { get; } = new();

        public FakeAuthorizationRepository AuthorizationRepository { get; } = new();

        public FakeReadRepository ReadRepository { get; } = new();

        public void AuthorizeManaged(EntityId patientProfileId) =>
            AuthorizationRepository.Set(patientProfileId, true, EntityId.New());

        public ClinicalHistoryListItem CreateItem(int offset) =>
            new(
                EntityId.New(),
                ClinicalHistoryEventType.CompletedPreTriage,
                Now.AddMinutes(-offset),
                Now.AddMinutes(-offset).AddSeconds(5),
                AuthoritativeClinicalSourceType.PreTriageEpisode,
                EntityId.New(),
                EntityId.New(),
                EntityId.New());

        public Task<ListClinicalHistoryResult> ListAsync(
            EntityId patientProfileId,
            string? cursor = null,
            int? pageSize = null,
            string? eventType = null)
        {
            var authorizer = new AuthorizePatientAccess(
                new FakeClock(),
                Profiles.Resolver,
                AuthorizationRepository,
                Profiles.MyCircleAudit);
            return new ListClinicalHistory(authorizer, ReadRepository).ExecuteAsync(
                new ListClinicalHistoryQuery(
                    patientProfileId,
                    cursor,
                    pageSize,
                    eventType));
        }
    }

    private sealed class FakeAuthorizationRepository : IPatientAccessAuthorizationRepository
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

    private sealed class FakeReadRepository : IClinicalHistoryReadRepository
    {
        public IReadOnlyList<ClinicalHistoryListItem> Results { get; set; } = [];

        public int CallCount { get; private set; }

        public ClinicalHistoryEventType? EventType { get; private set; }

        public ClinicalHistoryPageCursor? After { get; private set; }

        public int Take { get; private set; }

        public bool CursorExists { get; set; } = true;

        public int CursorCheckCount { get; private set; }

        public Task<bool> CursorExistsAsync(
            ClinicalHistoryPageCursor cursor,
            CancellationToken cancellationToken = default)
        {
            CursorCheckCount++;
            return Task.FromResult(CursorExists);
        }

        public Task<IReadOnlyList<ClinicalHistoryListItem>> ListAsync(
            EntityId patientProfileId,
            ClinicalHistoryEventType? eventType,
            ClinicalHistoryPageCursor? after,
            int take,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            EventType = eventType;
            After = after;
            Take = take;
            return Task.FromResult(Results);
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow =>
            new(2026, 8, 23, 15, 0, 0, TimeSpan.Zero);
    }
}
