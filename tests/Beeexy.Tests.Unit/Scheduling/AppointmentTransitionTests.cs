using Beeexy.Application.Common;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Scheduling;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Scheduling;

public sealed class AppointmentTransitionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 31, 20, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequestedTransition_AppliesOnceWithSchedulerAudit(bool confirm)
    {
        var fixture = new Fixture();

        var first = await fixture.ExecuteAsync(confirm);
        var version = fixture.State.Appointment.Version;
        var historyCount = fixture.State.Appointment.StatusHistory.Count;
        var second = await fixture.ExecuteAsync(confirm);

        var expected = confirm ? AppointmentStatus.Confirmed : AppointmentStatus.Rejected;
        Assert.Equal(expected, first.Appointment.Status);
        Assert.True(first.NewlyApplied);
        Assert.Equal(expected, second.Appointment.Status);
        Assert.False(second.NewlyApplied);
        Assert.Equal(2, version);
        Assert.Equal(version, fixture.State.Appointment.Version);
        Assert.Equal(2, historyCount);
        Assert.Equal(historyCount, fixture.State.Appointment.StatusHistory.Count);
        Assert.Equal(confirm, fixture.State.Appointment.ReservesSlot);
        var transition = Assert.Single(
            fixture.State.Appointment.StatusHistory.Where(value => value.Sequence == 2));
        Assert.Equal(AppointmentStatus.Requested, transition.PreviousStatus);
        Assert.Equal(expected, transition.NewStatus);
        Assert.Equal(AppointmentActorType.AppointmentScheduler, transition.ActorType);
        Assert.Equal(
            confirm ? AppointmentStatusAction.Confirmation : AppointmentStatusAction.Rejection,
            transition.Action);
        Assert.Equal(fixture.Profile.Account.Id, transition.ActorAccountId);
        Assert.Equal(Now, transition.OccurredAt);
    }

    [Fact]
    public async Task WrongClinicScheduler_IsForbiddenWithoutMutation()
    {
        var fixture = new Fixture(assignedClinics: [EntityId.New()]);

        await Assert.ThrowsAsync<AppointmentSchedulerForbiddenException>(() =>
            fixture.ExecuteAsync(confirm: true));

        Assert.Equal(AppointmentStatus.Requested, fixture.State.Appointment.Status);
        Assert.Equal(1, fixture.State.Appointment.Version);
        Assert.Single(fixture.State.Appointment.StatusHistory);
        Assert.Equal(0, fixture.Transaction.SaveCount);
    }

    [Fact]
    public async Task NoConfiguredScheduler_IsForbidden()
    {
        var fixture = new Fixture(assignScheduler: false);

        await Assert.ThrowsAsync<AppointmentSchedulerForbiddenException>(() =>
            fixture.ExecuteAsync(confirm: false));
    }

    [Fact]
    public void Assignments_AreStrictlyAccountAndClinicScoped()
    {
        var account = EntityId.New();
        var clinicA = EntityId.New();
        var clinicB = EntityId.New();
        var clinicC = EntityId.New();
        var otherAccount = EntityId.New();
        var assignments = AppointmentSchedulerAssignments.Create([
            new(account, [clinicA, clinicB]),
            new(otherAccount, [clinicA])
        ]);

        Assert.True(assignments.HasAppointmentSchedulerPermission(account, clinicA));
        Assert.True(assignments.HasAppointmentSchedulerPermission(account, clinicB));
        Assert.True(assignments.HasAppointmentSchedulerPermission(otherAccount, clinicA));
        Assert.False(assignments.HasAppointmentSchedulerPermission(account, clinicC));
        Assert.False(assignments.HasAppointmentSchedulerPermission(EntityId.New(), clinicA));
        Assert.False(AppointmentSchedulerAssignments.Empty
            .HasAppointmentSchedulerPermission(account, clinicA));
    }

    [Fact]
    public async Task OppositeTransition_IsConflictWithoutMutation()
    {
        var fixture = new Fixture();
        await fixture.ExecuteAsync(confirm: true);
        var version = fixture.State.Appointment.Version;
        var count = fixture.State.Appointment.StatusHistory.Count;

        await Assert.ThrowsAsync<AppointmentTransitionConflictException>(() =>
            fixture.ExecuteAsync(confirm: false));

        Assert.Equal(AppointmentStatus.Confirmed, fixture.State.Appointment.Status);
        Assert.Equal(version, fixture.State.Appointment.Version);
        Assert.Equal(count, fixture.State.Appointment.StatusHistory.Count);
        Assert.Equal(1, fixture.Transaction.SaveCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SameActionConcurrency_ReloadsAsIdempotentSuccess(bool confirm)
    {
        var fixture = new Fixture();
        fixture.Transaction.ConcurrencyReload = fixture.CreateAppointment();
        if (confirm)
        {
            fixture.Transaction.ConcurrencyReload.Confirm(fixture.Profile.Account.Id, Now);
        }
        else
        {
            fixture.Transaction.ConcurrencyReload.Reject(fixture.Profile.Account.Id, Now);
        }

        var result = await fixture.ExecuteAsync(confirm);

        Assert.False(result.NewlyApplied);
        Assert.Equal(
            confirm ? AppointmentStatus.Confirmed : AppointmentStatus.Rejected,
            result.Appointment.Status);
        Assert.Equal(2, fixture.Transaction.ConcurrencyReload.StatusHistory.Count);
    }

    [Fact]
    public async Task IncompatibleConcurrency_ReloadsAsConflict()
    {
        var fixture = new Fixture();
        fixture.Transaction.ConcurrencyReload = fixture.CreateAppointment();
        fixture.Transaction.ConcurrencyReload.Reject(fixture.Profile.Account.Id, Now);

        await Assert.ThrowsAsync<AppointmentTransitionConflictException>(() =>
            fixture.ExecuteAsync(confirm: true));
    }

    private sealed class Fixture
    {
        private readonly StubClock clock = new(Now);

        public Fixture(
            bool assignScheduler = true,
            IReadOnlyCollection<EntityId>? assignedClinics = null)
        {
            Profile = new MyCircleListingTestFixture();
            var slot = AvailabilitySlot.Create(
                EntityId.New(),
                EntityId.New(),
                EntityId.New(),
                Now.AddHours(2),
                Now.AddHours(2).AddMinutes(30),
                IanaTimeZone.Create("America/Lima"),
                AppointmentModality.InPerson,
                true,
                Now.AddDays(-1));
            State = new AppointmentTransitionState(CreateAppointment(slot), slot);
            Transaction = new FakeTransaction(State);
            var assignments = assignScheduler
                ? AppointmentSchedulerAssignments.Create([
                    new AppointmentSchedulerAssignment(
                        Profile.Account.Id,
                        assignedClinics ?? [slot.ClinicId])])
                : AppointmentSchedulerAssignments.Empty;
            var transition = new TransitionAppointment(
                clock,
                Profile.Resolver,
                assignments,
                Transaction);
            Confirm = new ConfirmAppointment(transition);
            Reject = new RejectAppointment(transition);
        }

        public MyCircleListingTestFixture Profile { get; }

        public AppointmentTransitionState State { get; }

        public FakeTransaction Transaction { get; }

        private ConfirmAppointment Confirm { get; }

        private RejectAppointment Reject { get; }

        public Task<AppointmentTransitionResult> ExecuteAsync(bool confirm) => confirm
            ? Confirm.ExecuteAsync(State.Appointment.Id)
            : Reject.ExecuteAsync(State.Appointment.Id);

        public Appointment CreateAppointment() => CreateAppointment(State.Slot);

        private Appointment CreateAppointment(AvailabilitySlot slot) => Appointment.Create(
            Profile.PrimaryProfile.Id,
            slot,
            Profile.Account.Id,
            AppointmentModality.InPerson,
            AppointmentReason.Create("Not exposed to scheduler"),
            EntityId.New(),
            AppointmentRequestFingerprint.Create(
                Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
            Now.AddMinutes(-1),
            StateOrNewId());

        private EntityId StateOrNewId() =>
            State?.Appointment.Id ?? EntityId.New();
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeTransaction(AppointmentTransitionState state)
        : IAppointmentTransitionTransaction
    {
        public Appointment? ConcurrencyReload { get; set; }

        public int SaveCount { get; private set; }

        public Task BeginAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AppointmentTransitionState?> LoadAsync(
            EntityId appointmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AppointmentTransitionState?>(state);

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (ConcurrencyReload is not null)
            {
                throw new AppointmentTransitionConcurrencyException(
                    new InvalidOperationException("Simulated concurrency."));
            }

            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AppointmentTransitionState?> ReloadAsync(
            EntityId appointmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AppointmentTransitionState?>(ConcurrencyReload is null
                ? null
                : new AppointmentTransitionState(ConcurrencyReload, state.Slot));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
