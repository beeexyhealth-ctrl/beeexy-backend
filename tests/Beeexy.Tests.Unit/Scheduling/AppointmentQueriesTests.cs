using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Scheduling;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Scheduling;

public sealed class AppointmentQueriesTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OwnerList_UsesPrimaryProfileAndReturnsOpaqueKeysetPage()
    {
        var fixture = new Fixture();
        fixture.Repository.Items.AddRange([
            fixture.Summary(Now.AddHours(1)),
            fixture.Summary(Now.AddHours(1)),
            fixture.Summary(Now.AddHours(2))
        ]);

        var result = await fixture.List.ExecuteAsync(new ListAppointmentsQuery(PageSize: 2));

        Assert.Equal(2, result.Items.Count);
        Assert.NotNull(result.NextCursor);
        Assert.DoesNotContain(result.Items[0].AppointmentId.Value.ToString("D"), result.NextCursor!);
        Assert.Equal(fixture.Profile.PrimaryProfile.Id, fixture.Repository.PrimaryProfileId);
        Assert.Equal(3, fixture.Repository.Take);
    }

    [Fact]
    public async Task Cursor_IsBoundToFilterContext()
    {
        var fixture = new Fixture();
        fixture.Repository.Items.AddRange([
            fixture.Summary(Now.AddHours(1)),
            fixture.Summary(Now.AddHours(2))
        ]);
        var first = await fixture.List.ExecuteAsync(new ListAppointmentsQuery(
            Status: "Requested",
            PageSize: 1));

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.List.ExecuteAsync(new ListAppointmentsQuery(
                Status: "Confirmed",
                Cursor: first.NextCursor,
                PageSize: 1)));

        Assert.Equal("scheduling.appointment_cursor_invalid", exception.Code);
    }

    [Theory]
    [InlineData("requested", "scheduling.appointment_status_invalid")]
    [InlineData("Unknown", "scheduling.appointment_status_invalid")]
    public async Task InvalidStatus_IsRejected(string status, string expectedCode)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.List.ExecuteAsync(new ListAppointmentsQuery(Status: status)));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task InvalidRangeAndPageSize_AreRejected()
    {
        var fixture = new Fixture();

        var range = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.List.ExecuteAsync(new ListAppointmentsQuery(From: Now, To: Now)));
        var page = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.List.ExecuteAsync(new ListAppointmentsQuery(PageSize: 101)));

        Assert.Equal("scheduling.appointment_range_invalid", range.Code);
        Assert.Equal("scheduling.appointment_page_size_invalid", page.Code);
    }

    [Fact]
    public async Task ActiveManagerPatientFilter_IsAuthorized()
    {
        var fixture = new Fixture();
        var patientId = EntityId.New();
        fixture.Authorization.TargetExists = true;
        fixture.Authorization.RelationshipId = EntityId.New();

        await fixture.List.ExecuteAsync(new ListAppointmentsQuery(patientId));

        Assert.Equal(patientId, fixture.Authorization.TargetProfileId);
        Assert.Equal(patientId, fixture.Repository.Filter?.PatientProfileId);
    }

    [Fact]
    public async Task RevokedOrUnrelatedPatientFilter_IsConcealed()
    {
        var fixture = new Fixture();
        fixture.Authorization.TargetExists = true;

        await Assert.ThrowsAsync<AppointmentNotFoundException>(() =>
            fixture.List.ExecuteAsync(new ListAppointmentsQuery(EntityId.New())));

        Assert.Null(fixture.Repository.Filter);
    }

    [Fact]
    public async Task Detail_AuthorizesAppointmentPatientAndReturnsSeparateHistories()
    {
        var fixture = new Fixture();
        var appointmentId = EntityId.New();
        fixture.Repository.PatientProfileId = fixture.Profile.PrimaryProfile.Id;
        fixture.Repository.Detail = fixture.Detail(appointmentId);

        var result = await fixture.Get.ExecuteAsync(appointmentId);

        Assert.Equal(appointmentId, result.Appointment.AppointmentId);
        Assert.Single(result.StatusHistory);
        Assert.Single(result.RescheduleHistory);
        Assert.Equal(AppointmentStatusAction.Creation, result.StatusHistory[0].Action);
    }

    [Fact]
    public async Task DetailForInaccessiblePatient_UsesSameNotFoundAsMissingAppointment()
    {
        var inaccessible = new Fixture();
        inaccessible.Repository.PatientProfileId = EntityId.New();
        inaccessible.Authorization.TargetExists = true;
        var missing = new Fixture();

        var denied = await Assert.ThrowsAsync<AppointmentNotFoundException>(() =>
            inaccessible.Get.ExecuteAsync(EntityId.New()));
        var absent = await Assert.ThrowsAsync<AppointmentNotFoundException>(() =>
            missing.Get.ExecuteAsync(EntityId.New()));

        Assert.Equal(absent.Message, denied.Message);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            var authorize = new AuthorizePatientAccess(
                new StubClock(),
                Profile.Resolver,
                Authorization,
                Profile.MyCircleAudit);
            List = new ListAppointments(Profile.Resolver, authorize, Repository);
            Get = new GetAppointment(Profile.Resolver, authorize, Repository);
        }

        public MyCircleListingTestFixture Profile { get; } = new();

        public FakeAuthorizationRepository Authorization { get; } = new();

        public FakeReadRepository Repository { get; } = new();

        public ListAppointments List { get; }

        public GetAppointment Get { get; }

        public AppointmentSummary Summary(DateTimeOffset startsAt, EntityId? id = null) => new(
            id ?? EntityId.New(),
            Profile.PrimaryProfile.Id,
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            AppointmentStatus.Requested,
            AppointmentModality.InPerson,
            startsAt,
            startsAt.AddMinutes(30),
            "America/Lima",
            Now.AddDays(-1));

        public AppointmentDetail Detail(EntityId appointmentId)
        {
            var summary = Summary(Now.AddHours(1), appointmentId);
            return new AppointmentDetail(
                summary,
                "Private scheduling reason",
                [new AppointmentStatusHistoryItem(
                    1,
                    null,
                    AppointmentStatus.Requested,
                    AppointmentActorType.PatientAuthority,
                    AppointmentStatusAction.Creation,
                    Now.AddDays(-1))],
                [new AppointmentRescheduleHistoryItem(
                    EntityId.New(),
                    EntityId.New(),
                    Now)]);
        }
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeAuthorizationRepository : IPatientAccessAuthorizationRepository
    {
        public bool TargetExists { get; set; }

        public EntityId? RelationshipId { get; set; }

        public EntityId? TargetProfileId { get; private set; }

        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default)
        {
            TargetProfileId = targetProfileId;
            return Task.FromResult(new PatientAccessAuthorizationLookup(
                TargetExists,
                RelationshipId));
        }
    }

    private sealed class FakeReadRepository : IAppointmentReadRepository
    {
        public List<AppointmentSummary> Items { get; } = [];

        public EntityId? PrimaryProfileId { get; private set; }

        public AppointmentListFilter? Filter { get; private set; }

        public int Take { get; private set; }

        public EntityId? PatientProfileId { get; set; }

        public AppointmentDetail? Detail { get; set; }

        public Task<bool> CursorExistsAsync(
            EntityId accessiblePrimaryProfileId,
            AppointmentPageCursor cursor,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyList<AppointmentSummary>> ListAsync(
            EntityId accessiblePrimaryProfileId,
            AppointmentListFilter filter,
            AppointmentPageCursor? after,
            int take,
            CancellationToken cancellationToken = default)
        {
            PrimaryProfileId = accessiblePrimaryProfileId;
            Filter = filter;
            Take = take;
            return Task.FromResult<IReadOnlyList<AppointmentSummary>>(Items);
        }

        public Task<EntityId?> FindPatientProfileIdAsync(
            EntityId appointmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PatientProfileId);

        public Task<AppointmentDetail?> GetAsync(
            EntityId appointmentId,
            CancellationToken cancellationToken = default) => Task.FromResult(Detail);
    }
}
