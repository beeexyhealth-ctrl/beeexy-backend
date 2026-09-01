using System.Reflection;
using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Scheduling;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Scheduling;

[Trait("Category", "Phase8Acceptance")]
public sealed class CancelAppointmentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 1, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AppointmentStatus.Requested)]
    [InlineData(AppointmentStatus.Confirmed)]
    public async Task Owner_CancelsApprovedStateOnce(AppointmentStatus initialStatus)
    {
        var fixture = new Fixture(initialStatus);
        var initialVersion = fixture.State.Appointment.Version;
        var initialHistoryCount = fixture.State.Appointment.StatusHistory.Count;

        var first = await fixture.ExecuteAsync();
        var second = await fixture.ExecuteAsync();

        Assert.True(first.NewlyApplied);
        Assert.False(second.NewlyApplied);
        Assert.Equal(AppointmentStatus.Cancelled, first.Appointment.Status);
        Assert.Equal(initialVersion + 1, fixture.State.Appointment.Version);
        Assert.Equal(initialHistoryCount + 1, fixture.State.Appointment.StatusHistory.Count);
        Assert.False(fixture.State.Appointment.ReservesSlot);
        var cancellation = fixture.State.Appointment.StatusHistory.Last();
        Assert.Equal(initialStatus, cancellation.PreviousStatus);
        Assert.Equal(AppointmentStatus.Cancelled, cancellation.NewStatus);
        Assert.Equal(AppointmentStatusAction.Cancellation, cancellation.Action);
        Assert.Equal(AppointmentActorType.PatientAuthority, cancellation.ActorType);
        Assert.Equal(fixture.Profile.Account.Id, cancellation.ActorAccountId);
        Assert.Equal(Now, cancellation.OccurredAt);
    }

    [Fact]
    public async Task ActiveManager_CanCancelUnderLockedCurrentAuthority()
    {
        var fixture = new Fixture(
            AppointmentStatus.Requested,
            patientProfileId: EntityId.New());
        fixture.AuthorizationRepository.TargetExists = true;
        fixture.AuthorizationRepository.ActiveRelationshipId = EntityId.New();

        var result = await fixture.ExecuteAsync();

        Assert.Equal(AppointmentStatus.Cancelled, result.Appointment.Status);
        Assert.Equal(1, fixture.AuthorizationRepository.LockedFindCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrRevokedManagerAuthority_IsConcealed(bool targetExists)
    {
        var fixture = new Fixture(
            AppointmentStatus.Requested,
            patientProfileId: EntityId.New());
        fixture.AuthorizationRepository.TargetExists = targetExists;

        await Assert.ThrowsAsync<AppointmentNotFoundException>(() =>
            fixture.ExecuteAsync());

        Assert.Equal(AppointmentStatus.Requested, fixture.State.Appointment.Status);
        Assert.Equal(1, fixture.State.Appointment.Version);
        Assert.Single(fixture.State.Appointment.StatusHistory);
        Assert.Equal(0, fixture.Transaction.SaveCount);
    }

    [Theory]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task UnsupportedState_ConflictsWithoutMutation(AppointmentStatus status)
    {
        var fixture = new Fixture(status);
        var version = fixture.State.Appointment.Version;
        var historyCount = fixture.State.Appointment.StatusHistory.Count;

        await Assert.ThrowsAsync<AppointmentTransitionConflictException>(() =>
            fixture.ExecuteAsync());

        Assert.Equal(status, fixture.State.Appointment.Status);
        Assert.Equal(version, fixture.State.Appointment.Version);
        Assert.Equal(historyCount, fixture.State.Appointment.StatusHistory.Count);
        Assert.Equal(0, fixture.Transaction.SaveCount);
    }

    [Fact]
    public async Task ConcurrentCancellation_ReloadsAsAuthorizedIdempotentSuccess()
    {
        var fixture = new Fixture(AppointmentStatus.Requested);
        fixture.Transaction.ConcurrencyReload = fixture.CreateAppointment(
            AppointmentStatus.Cancelled);

        var result = await fixture.ExecuteAsync();

        Assert.False(result.NewlyApplied);
        Assert.Equal(AppointmentStatus.Cancelled, result.Appointment.Status);
        Assert.Equal(2, fixture.Transaction.ConcurrencyReload.StatusHistory.Count);
    }

    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Rejected)]
    public async Task ConcurrentIncompatibleTransition_ReloadsAsConflict(
        AppointmentStatus winningStatus)
    {
        var fixture = new Fixture(AppointmentStatus.Requested);
        fixture.Transaction.ConcurrencyReload = fixture.CreateAppointment(winningStatus);

        await Assert.ThrowsAsync<AppointmentTransitionConflictException>(() =>
            fixture.ExecuteAsync());
    }

    private sealed class Fixture
    {
        private readonly StubClock clock = new(Now);

        public Fixture(
            AppointmentStatus initialStatus,
            EntityId? patientProfileId = null)
        {
            Profile = new MyCircleListingTestFixture();
            PatientProfileId = patientProfileId ?? Profile.PrimaryProfile.Id;
            var slot = AvailabilitySlot.Create(
                EntityId.New(),
                EntityId.New(),
                EntityId.New(),
                Now.AddMinutes(5),
                Now.AddMinutes(35),
                IanaTimeZone.Create("America/Lima"),
                AppointmentModality.InPerson,
                true,
                Now.AddDays(-1));
            AppointmentId = EntityId.New();
            State = new AppointmentTransitionState(
                CreateAppointment(initialStatus, slot),
                slot);
            Transaction = new FakeTransaction(State);
            AuthorizationRepository = new StubAuthorizationRepository();
            var authorization = new AuthorizePatientAccess(
                clock,
                Profile.Resolver,
                AuthorizationRepository,
                Profile.MyCircleAudit);
            UseCase = new CancelAppointment(
                clock,
                Profile.Resolver,
                authorization,
                Transaction);
        }

        public MyCircleListingTestFixture Profile { get; }

        public EntityId PatientProfileId { get; }

        public EntityId AppointmentId { get; }

        public AppointmentTransitionState State { get; }

        public StubAuthorizationRepository AuthorizationRepository { get; }

        public FakeTransaction Transaction { get; }

        private CancelAppointment UseCase { get; }

        public Task<AppointmentTransitionResult> ExecuteAsync() =>
            UseCase.ExecuteAsync(AppointmentId);

        public Appointment CreateAppointment(AppointmentStatus status) =>
            CreateAppointment(status, State.Slot);

        private Appointment CreateAppointment(
            AppointmentStatus status,
            AvailabilitySlot slot)
        {
            var appointment = Appointment.Create(
                PatientProfileId,
                slot,
                Profile.Account.Id,
                AppointmentModality.InPerson,
                AppointmentReason.Create("Sensitive reason remains outside cancellation output"),
                EntityId.New(),
                AppointmentRequestFingerprint.Create(
                    Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
                Now.AddHours(-1),
                AppointmentId);
            switch (status)
            {
                case AppointmentStatus.Requested:
                    break;
                case AppointmentStatus.Confirmed:
                    appointment.Confirm(Profile.Account.Id, Now.AddMinutes(-30));
                    break;
                case AppointmentStatus.Rejected:
                    appointment.Reject(Profile.Account.Id, Now.AddMinutes(-30));
                    break;
                case AppointmentStatus.Cancelled:
                    appointment.Cancel(Profile.Account.Id, Now.AddMinutes(-30));
                    break;
                case AppointmentStatus.Completed:
                case AppointmentStatus.NoShow:
                    typeof(Appointment)
                        .GetProperty(nameof(Appointment.Status), BindingFlags.Instance |
                            BindingFlags.Public)!
                        .SetValue(appointment, status);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status));
            }

            return appointment;
        }
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    internal sealed class StubAuthorizationRepository : IPatientAccessAuthorizationRepository
    {
        public bool TargetExists { get; set; }

        public EntityId? ActiveRelationshipId { get; set; }

        public int LockedFindCount { get; private set; }

        public Task<PatientAccessAuthorizationLookup> FindAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PatientAccessAuthorizationLookup(
                TargetExists,
                ActiveRelationshipId));

        public Task<PatientAccessAuthorizationLookup> FindForPatientUpdateAsync(
            EntityId managerProfileId,
            EntityId targetProfileId,
            CancellationToken cancellationToken = default)
        {
            LockedFindCount++;
            return FindAsync(managerProfileId, targetProfileId, cancellationToken);
        }
    }

    internal sealed class FakeTransaction(AppointmentTransitionState state)
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
