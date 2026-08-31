using System.Globalization;
using Beeexy.Application.Common;
using Beeexy.Application.Patients;
using Beeexy.Application.Scheduling;
using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Scheduling;
using Beeexy.Tests.Unit.Patients;

namespace Beeexy.Tests.Unit.Scheduling;

public sealed class RequestAppointmentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 31, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FirstRequest_CreatesRequestedAppointmentAndInitialHistory()
    {
        var fixture = new Fixture();

        var result = await fixture.ExecuteAsync(reason: "  Follow-up visit  ");

        Assert.True(result.NewlyCreated);
        Assert.Equal(AppointmentStatus.Requested, result.Appointment.Status);
        Assert.Equal("Follow-up visit", result.Appointment.Reason);
        var persisted = Assert.Single(fixture.Transaction.Added);
        var history = Assert.Single(persisted.StatusHistory);
        Assert.Equal(AppointmentStatusAction.Creation, history.Action);
        Assert.Equal(AppointmentActorType.PatientAuthority, history.ActorType);
        Assert.Equal(1, history.Sequence);
    }

    [Fact]
    public async Task ActiveManager_IsReauthorizedUnderTransactionAndCanBook()
    {
        var fixture = new Fixture();
        var managedPatientId = EntityId.New();
        fixture.AuthorizationRepository.TargetExists = true;
        fixture.AuthorizationRepository.ActiveRelationshipId = EntityId.New();

        var result = await fixture.ExecuteAsync(patientProfileId: managedPatientId);

        Assert.True(result.NewlyCreated);
        Assert.Equal(managedPatientId, result.Appointment.PatientProfileId);
        Assert.Equal(1, fixture.AuthorizationRepository.LockedFindCount);
    }

    [Fact]
    public async Task RevokedOrUnrelatedManager_IsConcealedBeforeTransaction()
    {
        var fixture = new Fixture();
        fixture.AuthorizationRepository.TargetExists = true;

        await Assert.ThrowsAsync<AppointmentNotFoundException>(() =>
            fixture.ExecuteAsync(patientProfileId: EntityId.New()));

        Assert.False(fixture.Transaction.Begun);
    }

    [Fact]
    public async Task ExactReplay_ReturnsOriginalWithoutCreatingAnotherAppointment()
    {
        var fixture = new Fixture();
        var original = fixture.CreateExisting(reason: "Follow-up visit");
        fixture.Transaction.Existing = original;

        var result = await fixture.ExecuteAsync(reason: "  Follow-up visit  ");

        Assert.False(result.NewlyCreated);
        Assert.Equal(original.Appointment.Id, result.Appointment.AppointmentId);
        Assert.Empty(fixture.Transaction.Added);
        Assert.True(fixture.Transaction.Committed);
    }

    [Fact]
    public async Task ReusedKeyWithDifferentSemanticInput_IsConflictBeforeSlotLookup()
    {
        var fixture = new Fixture();
        fixture.Transaction.Existing = fixture.CreateExisting(reason: "Original");

        await Assert.ThrowsAsync<AppointmentIdempotencyConflictException>(() =>
            fixture.ExecuteAsync(reason: "Changed"));

        Assert.Equal(0, fixture.Transaction.SlotLookupCount);
        Assert.Empty(fixture.Transaction.Added);
    }

    [Fact]
    public async Task HiddenDirectoryRelationship_UsesConcealedNotFoundSemantics()
    {
        var fixture = new Fixture();
        fixture.Transaction.SlotState = new AppointmentSlotRequestState(
            fixture.Slot,
            HasEligibleDirectoryRelationships: false);

        await Assert.ThrowsAsync<AppointmentNotFoundException>(() => fixture.ExecuteAsync());
    }

    [Fact]
    public async Task PastSlot_IsRejectedWithoutPersisting()
    {
        var fixture = new Fixture(slotStartsAt: Now);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.ExecuteAsync());

        Assert.Equal("scheduling.slot_expired", exception.Code);
        Assert.Empty(fixture.Transaction.Added);
    }

    [Fact]
    public async Task ModalityMismatch_IsRejectedWithoutPersisting()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.ExecuteAsync(modality: AppointmentModality.Virtual));

        Assert.Equal("scheduling.modality_mismatch", exception.Code);
        Assert.Empty(fixture.Transaction.Added);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankReason_IsRejected(string reason)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            fixture.ExecuteAsync(reason: reason));

        Assert.Equal("scheduling.reason_invalid", exception.Code);
    }

    [Fact]
    public async Task FiveHundredCharacterReason_IsAcceptedAndLongerReasonIsRejected()
    {
        var accepted = new Fixture();
        var result = await accepted.ExecuteAsync(reason: new string('r', 500));
        Assert.Equal(500, result.Appointment.Reason?.Length);

        var rejected = new Fixture();
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            rejected.ExecuteAsync(reason: new string('r', 501)));
        Assert.Equal("scheduling.reason_invalid", exception.Code);
        Assert.Empty(rejected.Transaction.Added);
    }

    [Fact]
    public async Task VirtualSlot_AcceptsVirtualRequest()
    {
        var fixture = new Fixture(slotModality: AppointmentModality.Virtual);

        var result = await fixture.ExecuteAsync(modality: AppointmentModality.Virtual);

        Assert.Equal(AppointmentModality.Virtual, result.Appointment.Modality);
    }

    [Fact]
    public void Fingerprint_IsCultureIndependentAndChangesWithEverySemanticField()
    {
        var patient = EntityId.From(Guid.Parse("81000000-0000-4000-8000-000000000001"));
        var slot = EntityId.From(Guid.Parse("81000000-0000-4000-8000-000000000002"));
        var baseline = AppointmentRequestFingerprintCalculator.Calculate(
            patient,
            slot,
            AppointmentModality.InPerson,
            AppointmentReason.Create("Control"));
        var priorCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(
                baseline,
                AppointmentRequestFingerprintCalculator.Calculate(
                    patient,
                    slot,
                    AppointmentModality.InPerson,
                    AppointmentReason.Create("Control")));
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
        }

        Assert.NotEqual(baseline, AppointmentRequestFingerprintCalculator.Calculate(
            EntityId.New(), slot, AppointmentModality.InPerson, AppointmentReason.Create("Control")));
        Assert.NotEqual(baseline, AppointmentRequestFingerprintCalculator.Calculate(
            patient, EntityId.New(), AppointmentModality.InPerson, AppointmentReason.Create("Control")));
        Assert.NotEqual(baseline, AppointmentRequestFingerprintCalculator.Calculate(
            patient, slot, AppointmentModality.Virtual, AppointmentReason.Create("Control")));
        Assert.NotEqual(baseline, AppointmentRequestFingerprintCalculator.Calculate(
            patient, slot, AppointmentModality.InPerson, AppointmentReason.Create("Changed")));
        Assert.Matches("^[0-9a-f]{64}$", baseline.Value);
    }

    private sealed class Fixture
    {
        private readonly MyCircleListingTestFixture profile = new();
        private readonly StubClock clock = new(Now);

        public Fixture(
            DateTimeOffset? slotStartsAt = null,
            AppointmentModality slotModality = AppointmentModality.InPerson)
        {
            Slot = AvailabilitySlot.Create(
                EntityId.New(),
                EntityId.New(),
                EntityId.New(),
                slotStartsAt ?? Now.AddHours(2),
                (slotStartsAt ?? Now.AddHours(2)).AddMinutes(30),
                IanaTimeZone.Create("America/Lima"),
                slotModality,
                isPublished: true,
                Now.AddDays(-1));
            Transaction = new FakeTransaction
            {
                SlotState = new AppointmentSlotRequestState(
                    Slot,
                    HasEligibleDirectoryRelationships: true)
            };
            AuthorizationRepository = new StubAuthorizationRepository();
            var authorization = new AuthorizePatientAccess(
                clock,
                profile.Resolver,
                AuthorizationRepository,
                profile.MyCircleAudit);
            UseCase = new RequestAppointment(
                clock,
                profile.Resolver,
                authorization,
                Transaction);
        }

        public AvailabilitySlot Slot { get; }

        public FakeTransaction Transaction { get; }

        public StubAuthorizationRepository AuthorizationRepository { get; }

        private RequestAppointment UseCase { get; }

        private EntityId IdempotencyKey { get; } = EntityId.New();

        public Task<RequestAppointmentResult> ExecuteAsync(
            string? reason = null,
            AppointmentModality modality = AppointmentModality.InPerson,
            EntityId? patientProfileId = null) =>
            UseCase.ExecuteAsync(new RequestAppointmentCommand(
                patientProfileId ?? profile.PrimaryProfile.Id,
                Slot.Id,
                modality,
                reason,
                IdempotencyKey));

        public AppointmentRequestState CreateExisting(string? reason)
        {
            var normalizedReason = reason is null ? null : AppointmentReason.Create(reason);
            var appointment = Appointment.Create(
                profile.PrimaryProfile.Id,
                Slot,
                profile.Account.Id,
                AppointmentModality.InPerson,
                normalizedReason,
                IdempotencyKey,
                AppointmentRequestFingerprintCalculator.Calculate(
                    profile.PrimaryProfile.Id,
                    Slot.Id,
                    AppointmentModality.InPerson,
                    normalizedReason),
                Now.AddMinutes(-1));
            return new AppointmentRequestState(appointment, Slot);
        }
    }

    private sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
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

    private sealed class FakeTransaction : IAppointmentRequestTransaction
    {
        public AppointmentRequestState? Existing { get; set; }

        public AppointmentSlotRequestState? SlotState { get; set; }

        public List<Appointment> Added { get; } = [];

        public int SlotLookupCount { get; private set; }

        public bool Committed { get; private set; }

        public bool Begun { get; private set; }

        public Task BeginAsync(
            EntityId requestingAccountId,
            EntityId idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Begun = true;
            return Task.CompletedTask;
        }

        public Task<AppointmentRequestState?> FindExistingAsync(
            EntityId requestingAccountId,
            EntityId idempotencyKey,
            CancellationToken cancellationToken = default) => Task.FromResult(Existing);

        public Task<AppointmentSlotRequestState?> FindSlotAsync(
            EntityId slotId,
            CancellationToken cancellationToken = default)
        {
            SlotLookupCount++;
            return Task.FromResult(SlotState);
        }

        public void Add(Appointment appointment) => Added.Add(appointment);

        public Task<AppointmentRequestSaveResult> SaveAsync(
            Appointment appointment,
            AvailabilitySlot slot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppointmentRequestSaveResult(
                new AppointmentRequestState(appointment, slot),
                NewlyCreated: true));

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
