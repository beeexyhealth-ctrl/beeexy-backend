using System.Reflection;
using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Scheduling;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Scheduling;

public sealed class RescheduleAppointmentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 3, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AppointmentStatus.Requested)]
    [InlineData(AppointmentStatus.Confirmed)]
    public async Task ApprovedState_MovesOnceWithSeparateAudit(AppointmentStatus status)
    {
        var fixture = new Fixture(status);
        var originalId = fixture.State.Appointment.Id;
        var originalStatusHistoryCount = fixture.State.Appointment.StatusHistory.Count;
        var originalVersion = fixture.State.Appointment.Version;
        var originalSlotId = fixture.State.Appointment.AvailabilitySlotId;

        var result = await fixture.ExecuteAsync();

        Assert.True(result.NewlyApplied);
        Assert.Equal(originalId, result.Appointment.AppointmentId);
        Assert.Equal(status, result.Appointment.Status);
        Assert.Equal(fixture.TargetSlot.Id, result.Appointment.AvailabilitySlotId);
        Assert.Equal(fixture.TargetSlot.DoctorId, result.Appointment.DoctorId);
        Assert.Equal(fixture.TargetSlot.ClinicId, result.Appointment.ClinicId);
        Assert.Equal(fixture.TargetSlot.ClinicLocationId, result.Appointment.ClinicLocationId);
        Assert.Equal("America/New_York", result.Appointment.ClinicTimeZone);
        Assert.Equal(originalVersion + 1, fixture.State.Appointment.Version);
        Assert.Equal(originalStatusHistoryCount, fixture.State.Appointment.StatusHistory.Count);
        var audit = Assert.Single(fixture.Transaction.AddedHistory);
        Assert.Equal(originalId, audit.AppointmentId);
        Assert.Equal(originalSlotId, audit.PreviousSlotId);
        Assert.Equal(fixture.TargetSlot.Id, audit.NewSlotId);
        Assert.Equal(fixture.Profile.Account.Id, audit.ActorAccountId);
        Assert.Equal(Now, audit.OccurredAt);
    }

    [Fact]
    public async Task SameSlot_IsNoOpWithoutVersionOrHistoryMutation()
    {
        var fixture = new Fixture(AppointmentStatus.Requested);
        fixture.Transaction.Target = new AppointmentRescheduleTargetState(
            fixture.State.Slot,
            HasEligibleDirectoryRelationships: true,
            IsReserved: true);
        var version = fixture.State.Appointment.Version;
        var statusHistoryCount = fixture.State.Appointment.StatusHistory.Count;

        var result = await fixture.ExecuteAsync(fixture.State.Slot.Id);

        Assert.False(result.NewlyApplied);
        Assert.Equal(version, fixture.State.Appointment.Version);
        Assert.Equal(statusHistoryCount, fixture.State.Appointment.StatusHistory.Count);
        Assert.Empty(fixture.Transaction.AddedHistory);
        Assert.Equal(0, fixture.Transaction.SaveCount);
    }

    [Fact]
    public async Task ActiveManager_UsesLockedCurrentAuthority()
    {
        var fixture = new Fixture(
            AppointmentStatus.Requested,
            patientProfileId: EntityId.New());
        fixture.AuthorizationRepository.TargetExists = true;
        fixture.AuthorizationRepository.ActiveRelationshipId = EntityId.New();

        var result = await fixture.ExecuteAsync();

        Assert.True(result.NewlyApplied);
        Assert.Equal(1, fixture.AuthorizationRepository.LockedFindCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrRevokedAuthority_IsConcealed(bool targetExists)
    {
        var fixture = new Fixture(
            AppointmentStatus.Requested,
            patientProfileId: EntityId.New());
        fixture.AuthorizationRepository.TargetExists = targetExists;

        await Assert.ThrowsAsync<AppointmentNotFoundException>(() =>
            fixture.ExecuteAsync());

        Assert.Equal(fixture.State.Slot.Id, fixture.State.Appointment.AvailabilitySlotId);
        Assert.Empty(fixture.Transaction.AddedHistory);
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task InvalidSourceState_ConflictsWithoutMutation(AppointmentStatus status)
    {
        var fixture = new Fixture(status);
        var version = fixture.State.Appointment.Version;
        var statusHistoryCount = fixture.State.Appointment.StatusHistory.Count;

        await Assert.ThrowsAsync<AppointmentRescheduleConflictException>(() =>
            fixture.ExecuteAsync());

        Assert.Equal(fixture.State.Slot.Id, fixture.State.Appointment.AvailabilitySlotId);
        Assert.Equal(status, fixture.State.Appointment.Status);
        Assert.Equal(version, fixture.State.Appointment.Version);
        Assert.Equal(statusHistoryCount, fixture.State.Appointment.StatusHistory.Count);
        Assert.Empty(fixture.Transaction.AddedHistory);
    }

    [Theory]
    [InlineData("missing", null)]
    [InlineData("ineligible", null)]
    [InlineData("unpublished", "scheduling.slot_unbookable")]
    [InlineData("expired", "scheduling.slot_expired")]
    [InlineData("modality", "scheduling.modality_mismatch")]
    [InlineData("occupied", "conflict")]
    public async Task InvalidTarget_IsRejectedBeforeMutation(string kind, string? code)
    {
        var fixture = new Fixture(AppointmentStatus.Requested);
        fixture.ConfigureTarget(kind);
        var version = fixture.State.Appointment.Version;

        if (kind is "missing" or "ineligible")
        {
            await Assert.ThrowsAsync<AppointmentNotFoundException>(() =>
                fixture.ExecuteAsync());
        }
        else if (kind == "occupied")
        {
            await Assert.ThrowsAsync<AppointmentSlotReservationConflictException>(() =>
                fixture.ExecuteAsync());
        }
        else
        {
            var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
                fixture.ExecuteAsync());
            Assert.Equal(code, exception.Code);
        }

        Assert.Equal(fixture.State.Slot.Id, fixture.State.Appointment.AvailabilitySlotId);
        Assert.Equal(version, fixture.State.Appointment.Version);
        Assert.Empty(fixture.Transaction.AddedHistory);
    }

    [Fact]
    public async Task ConcurrentIdenticalReschedule_ReloadsAsIdempotentSuccess()
    {
        var fixture = new Fixture(AppointmentStatus.Requested);
        fixture.Transaction.ConcurrencyReload = fixture.CreateAppointmentOnSlot(
            AppointmentStatus.Requested,
            fixture.TargetSlot);

        var result = await fixture.ExecuteAsync();

        Assert.False(result.NewlyApplied);
        Assert.Equal(fixture.TargetSlot.Id, result.Appointment.AvailabilitySlotId);
    }

    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task ConcurrentIncompatibleMutation_ReloadsAsConflict(
        AppointmentStatus winningStatus)
    {
        var fixture = new Fixture(AppointmentStatus.Requested);
        fixture.Transaction.ConcurrencyReload = fixture.CreateAppointmentOnSlot(
            winningStatus,
            fixture.State.Slot);

        await Assert.ThrowsAsync<AppointmentRescheduleConflictException>(() =>
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
            AppointmentId = EntityId.New();
            var sourceSlot = CreateSlot(
                AppointmentModality.InPerson,
                Now.AddHours(2),
                "America/Lima");
            TargetSlot = CreateSlot(
                AppointmentModality.InPerson,
                Now.AddHours(4),
                "America/New_York");
            State = new AppointmentTransitionState(
                CreateAppointmentOnSlot(initialStatus, sourceSlot),
                sourceSlot);
            Transaction = new FakeTransaction(State)
            {
                Target = new AppointmentRescheduleTargetState(
                    TargetSlot,
                    HasEligibleDirectoryRelationships: true,
                    IsReserved: false)
            };
            AuthorizationRepository = new StubAuthorizationRepository();
            var authorization = new AuthorizePatientAccess(
                clock,
                Profile.Resolver,
                AuthorizationRepository,
                Profile.MyCircleAudit);
            UseCase = new RescheduleAppointment(
                clock,
                Profile.Resolver,
                authorization,
                Transaction);
        }

        public MyCircleListingTestFixture Profile { get; }

        public EntityId PatientProfileId { get; }

        public EntityId AppointmentId { get; }

        public AppointmentTransitionState State { get; }

        public AvailabilitySlot TargetSlot { get; private set; }

        public StubAuthorizationRepository AuthorizationRepository { get; }

        public FakeTransaction Transaction { get; }

        private RescheduleAppointment UseCase { get; }

        public Task<AppointmentTransitionResult> ExecuteAsync(EntityId? targetSlotId = null) =>
            UseCase.ExecuteAsync(AppointmentId, targetSlotId ?? TargetSlot.Id);

        public void ConfigureTarget(string kind)
        {
            switch (kind)
            {
                case "missing":
                    Transaction.Target = null;
                    break;
                case "ineligible":
                    Transaction.Target = Transaction.Target! with
                    {
                        HasEligibleDirectoryRelationships = false
                    };
                    break;
                case "unpublished":
                    TargetSlot = CreateSlot(
                        AppointmentModality.InPerson,
                        Now.AddHours(4),
                        "America/Lima",
                        isPublished: false);
                    SetTarget();
                    break;
                case "expired":
                    TargetSlot = CreateSlot(
                        AppointmentModality.InPerson,
                        Now,
                        "America/Lima");
                    SetTarget();
                    break;
                case "modality":
                    TargetSlot = CreateSlot(
                        AppointmentModality.Virtual,
                        Now.AddHours(4),
                        "America/Lima");
                    SetTarget();
                    break;
                case "occupied":
                    Transaction.Target = Transaction.Target! with { IsReserved = true };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public Appointment CreateAppointmentOnSlot(
            AppointmentStatus status,
            AvailabilitySlot slot)
        {
            var appointment = Appointment.Create(
                PatientProfileId,
                slot,
                Profile.Account.Id,
                AppointmentModality.InPerson,
                AppointmentReason.Create("Sensitive reschedule reason"),
                EntityId.New(),
                AppointmentRequestFingerprint.Create(
                    Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
                Now.AddHours(-2),
                AppointmentId);
            switch (status)
            {
                case AppointmentStatus.Requested:
                    break;
                case AppointmentStatus.Confirmed:
                    appointment.Confirm(Profile.Account.Id, Now.AddHours(-1));
                    break;
                case AppointmentStatus.Rejected:
                    appointment.Reject(Profile.Account.Id, Now.AddHours(-1));
                    break;
                case AppointmentStatus.Cancelled:
                    appointment.Cancel(Profile.Account.Id, Now.AddHours(-1));
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

        private void SetTarget() => Transaction.Target =
            new AppointmentRescheduleTargetState(
                TargetSlot,
                HasEligibleDirectoryRelationships: true,
                IsReserved: false);

        private static AvailabilitySlot CreateSlot(
            AppointmentModality modality,
            DateTimeOffset startsAt,
            string timezone,
            bool isPublished = true) => AvailabilitySlot.Create(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            startsAt,
            startsAt.AddMinutes(30),
            IanaTimeZone.Create(timezone),
            modality,
            isPublished,
            Now.AddDays(-1));
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
        : IAppointmentRescheduleTransaction
    {
        public AppointmentRescheduleTargetState? Target { get; set; }

        public Appointment? ConcurrencyReload { get; set; }

        public List<AppointmentRescheduleHistory> AddedHistory { get; } = [];

        public int SaveCount { get; private set; }

        public Task BeginAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AppointmentTransitionState?> LoadAsync(
            EntityId appointmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AppointmentTransitionState?>(state);

        public Task<AppointmentRescheduleTargetState?> FindTargetSlotAsync(
            EntityId slotId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Target);

        public void Add(AppointmentRescheduleHistory history) =>
            AddedHistory.Add(history);

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
            CancellationToken cancellationToken = default)
        {
            if (ConcurrencyReload is null)
            {
                return Task.FromResult<AppointmentTransitionState?>(null);
            }

            var slot = ConcurrencyReload.AvailabilitySlotId == Target?.Slot.Id
                ? Target.Slot
                : state.Slot;
            return Task.FromResult<AppointmentTransitionState?>(
                new AppointmentTransitionState(ConcurrencyReload, slot));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
