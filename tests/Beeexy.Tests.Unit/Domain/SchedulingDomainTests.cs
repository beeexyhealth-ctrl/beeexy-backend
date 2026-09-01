using Beeexy.Domain.Common;
using Beeexy.Domain.Directory;
using Beeexy.Domain.Scheduling;

namespace Beeexy.Tests.Unit.Domain;

[Trait("Category", "Phase8Acceptance")]
public sealed class SchedulingDomainTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 31, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OfficialStatusesAndModalities_AreRepresentedExactly()
    {
        Assert.Equal(
            ["Requested", "Confirmed", "Cancelled", "Completed", "NoShow", "Rejected"],
            Enum.GetNames<AppointmentStatus>());
        Assert.Equal(["InPerson", "Virtual"], Enum.GetNames<AppointmentModality>());
    }

    [Theory]
    [InlineData(AppointmentModality.InPerson)]
    [InlineData(AppointmentModality.Virtual)]
    public void AvailabilitySlot_PreservesExplicitRangeTimezoneAndModality(
        AppointmentModality modality)
    {
        var slot = CreateSlot(modality, TimeSpan.FromMinutes(45));

        Assert.Equal(TimeSpan.FromMinutes(45), slot.Duration);
        Assert.Equal("America/Lima", slot.ClinicTimeZone.Value);
        Assert.Equal(modality, slot.Modality);
        Assert.True(slot.IsPublished);
    }

    [Fact]
    public void AvailabilitySlot_RejectsInvalidTimeRangeRelationshipsModalityAndUtcValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSlot(
            AppointmentModality.InPerson,
            TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSlot(
            AppointmentModality.InPerson,
            TimeSpan.FromMinutes(-1)));
        Assert.Throws<ArgumentException>(() => AvailabilitySlot.Create(
            default,
            EntityId.New(),
            EntityId.New(),
            CreatedAt.AddHours(1),
            CreatedAt.AddHours(2),
            IanaTimeZone.Create("America/Lima"),
            AppointmentModality.InPerson,
            true,
            CreatedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSlot(
            (AppointmentModality)99,
            TimeSpan.FromMinutes(30)));
        Assert.Throws<ArgumentException>(() => AvailabilitySlot.Create(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            CreatedAt.AddHours(1).ToOffset(TimeSpan.FromHours(-5)),
            CreatedAt.AddHours(2),
            IanaTimeZone.Create("America/Lima"),
            AppointmentModality.InPerson,
            true,
            CreatedAt));
    }

    [Fact]
    public void IanaTimezone_RejectsUnknownValuesForSchedulingSlots()
    {
        Assert.Throws<ArgumentException>(() => IanaTimeZone.Create("Not/A_Timezone"));
    }

    [Fact]
    public void AppointmentReason_IsOptionalAndLimitedToFiveHundredCharacters()
    {
        var withoutReason = CreateAppointment(reason: null);
        var maximumReason = AppointmentReason.Create(new string('r', 500));
        var withReason = CreateAppointment(maximumReason);

        Assert.Null(withoutReason.Reason);
        Assert.NotNull(withReason.Reason);
        Assert.Equal(500, withReason.Reason.Value.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AppointmentReason.Create(new string('r', 501)));
    }

    [Fact]
    public void NewAppointment_StartsRequestedAndCreatesInitialOrderedHistory()
    {
        var appointment = CreateAppointment(reason: null);
        var history = Assert.Single(appointment.StatusHistory);

        Assert.Equal(AppointmentStatus.Requested, appointment.Status);
        Assert.True(appointment.ReservesSlot);
        Assert.Equal(1, appointment.Version);
        Assert.Equal(1, history.Sequence);
        Assert.Null(history.PreviousStatus);
        Assert.Equal(AppointmentStatus.Requested, history.NewStatus);
        Assert.Equal(AppointmentStatusAction.Creation, history.Action);
        Assert.Equal(AppointmentActorType.PatientAuthority, history.ActorType);
    }

    [Fact]
    public void Appointment_RequiresModalityToMatchItsSlot()
    {
        var slot = CreateSlot(AppointmentModality.InPerson, TimeSpan.FromMinutes(30));

        Assert.Throws<ArgumentException>(() => Appointment.Create(
            EntityId.New(),
            slot,
            EntityId.New(),
            AppointmentModality.Virtual,
            null,
            EntityId.New(),
            Fingerprint(),
            CreatedAt));
    }

    [Fact]
    public void SupportedTransitions_AppendOneHistoryRecordAndSameActionIsIdempotent()
    {
        var actor = EntityId.New();
        var confirmed = CreateAppointment(reason: null);

        Assert.True(confirmed.Confirm(actor, CreatedAt.AddMinutes(1)));
        Assert.False(confirmed.Confirm(actor, CreatedAt.AddMinutes(2)));
        Assert.Equal(AppointmentStatus.Confirmed, confirmed.Status);
        Assert.True(confirmed.ReservesSlot);
        Assert.Equal(2, confirmed.StatusHistory.Count);
        Assert.True(confirmed.Cancel(actor, CreatedAt.AddMinutes(3)));
        Assert.False(confirmed.Cancel(actor, CreatedAt.AddMinutes(4)));
        Assert.Equal(AppointmentStatus.Cancelled, confirmed.Status);
        Assert.False(confirmed.ReservesSlot);
        Assert.Equal([1L, 2L, 3L], confirmed.StatusHistory.Select(value => value.Sequence));

        var rejected = CreateAppointment(reason: null);
        Assert.True(rejected.Reject(actor, CreatedAt.AddMinutes(1)));
        Assert.False(rejected.Reject(actor, CreatedAt.AddMinutes(2)));
        Assert.Equal(AppointmentStatus.Rejected, rejected.Status);
        Assert.False(rejected.ReservesSlot);
        Assert.Throws<InvalidOperationException>(() =>
            rejected.Confirm(actor, CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() =>
            rejected.Cancel(actor, CreatedAt.AddMinutes(3)));
    }

    [Fact]
    public void OppositeAndCancelledSchedulerTransitions_DoNotMutateStateOrHistory()
    {
        var actor = EntityId.New();
        var confirmed = CreateAppointment(reason: null);
        confirmed.Confirm(actor, CreatedAt.AddMinutes(1));
        var confirmedVersion = confirmed.Version;
        var confirmedHistoryCount = confirmed.StatusHistory.Count;

        Assert.Throws<InvalidOperationException>(() =>
            confirmed.Reject(actor, CreatedAt.AddMinutes(2)));
        Assert.Equal(confirmedVersion, confirmed.Version);
        Assert.Equal(confirmedHistoryCount, confirmed.StatusHistory.Count);
        Assert.True(confirmed.ReservesSlot);

        var cancelled = CreateAppointment(reason: null);
        cancelled.Cancel(actor, CreatedAt.AddMinutes(1));
        var cancelledVersion = cancelled.Version;
        var cancelledHistoryCount = cancelled.StatusHistory.Count;

        Assert.Throws<InvalidOperationException>(() =>
            cancelled.Confirm(actor, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() =>
            cancelled.Reject(actor, CreatedAt.AddMinutes(2)));
        Assert.Equal(cancelledVersion, cancelled.Version);
        Assert.Equal(cancelledHistoryCount, cancelled.StatusHistory.Count);
        Assert.False(cancelled.ReservesSlot);
    }

    [Fact]
    public void AppointmentRequestFingerprint_RequiresCanonicalSha256Hex()
    {
        Assert.Equal(64, Fingerprint().Value.Length);
        Assert.Throws<ArgumentException>(() =>
            AppointmentRequestFingerprint.Create(new string('A', 64)));
        Assert.Throws<ArgumentException>(() =>
            AppointmentRequestFingerprint.Create(new string('a', 63)));
    }

    [Fact]
    public void RescheduleHistory_RequiresDistinctSlotsAndRetainsCurrentStatusSemantics()
    {
        var appointment = CreateAppointment(reason: null);
        var audit = AppointmentRescheduleHistory.Create(
            appointment.Id,
            appointment.AvailabilitySlotId,
            EntityId.New(),
            EntityId.New(),
            CreatedAt.AddMinutes(1));

        Assert.Equal(appointment.Id, audit.AppointmentId);
        Assert.Equal(AppointmentStatus.Requested, appointment.Status);
        Assert.Single(appointment.StatusHistory);
        Assert.Throws<ArgumentException>(() => AppointmentRescheduleHistory.Create(
            appointment.Id,
            appointment.AvailabilitySlotId,
            appointment.AvailabilitySlotId,
            EntityId.New(),
            CreatedAt.AddMinutes(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppointmentReschedule_PreservesIdentityStatusAndStatusHistory(bool confirmed)
    {
        var appointment = CreateAppointment(reason: null);
        var actor = EntityId.New();
        if (confirmed)
        {
            appointment.Confirm(actor, CreatedAt.AddMinutes(1));
        }

        var originalId = appointment.Id;
        var originalSlotId = appointment.AvailabilitySlotId;
        var originalStatus = appointment.Status;
        var originalStatusHistoryCount = appointment.StatusHistory.Count;
        var originalVersion = appointment.Version;
        var target = CreateSlot(AppointmentModality.InPerson, TimeSpan.FromMinutes(45));

        var audit = appointment.Reschedule(target, actor, CreatedAt.AddMinutes(2));

        Assert.NotNull(audit);
        Assert.Equal(originalId, appointment.Id);
        Assert.Equal(originalStatus, appointment.Status);
        Assert.Equal(target.Id, appointment.AvailabilitySlotId);
        Assert.Equal(target.StartsAt, appointment.ScheduledStartAt);
        Assert.Equal(originalVersion + 1, appointment.Version);
        Assert.Equal(originalStatusHistoryCount, appointment.StatusHistory.Count);
        Assert.Equal(originalSlotId, audit.PreviousSlotId);
        Assert.Equal(target.Id, audit.NewSlotId);
        Assert.Equal(actor, audit.ActorAccountId);
    }

    [Fact]
    public void SameSlotReschedule_IsNoOpAndModalityMismatchIsRejected()
    {
        var appointment = CreateAppointment(reason: null);
        var version = appointment.Version;
        var historyCount = appointment.StatusHistory.Count;
        var currentSlot = CreateSlot(AppointmentModality.InPerson, TimeSpan.FromMinutes(30));
        typeof(Appointment)
            .GetProperty(nameof(Appointment.AvailabilitySlotId))!
            .SetValue(appointment, currentSlot.Id);

        Assert.Null(appointment.Reschedule(
            currentSlot,
            EntityId.New(),
            CreatedAt.AddMinutes(1)));
        Assert.Equal(version, appointment.Version);
        Assert.Equal(historyCount, appointment.StatusHistory.Count);

        var virtualTarget = CreateSlot(
            AppointmentModality.Virtual,
            TimeSpan.FromMinutes(30));
        Assert.Throws<ArgumentException>(() => appointment.Reschedule(
            virtualTarget,
            EntityId.New(),
            CreatedAt.AddMinutes(1)));
        Assert.Equal(version, appointment.Version);
    }

    private static Appointment CreateAppointment(AppointmentReason? reason)
    {
        return Appointment.Create(
            EntityId.New(),
            CreateSlot(AppointmentModality.InPerson, TimeSpan.FromMinutes(30)),
            EntityId.New(),
            AppointmentModality.InPerson,
            reason,
            EntityId.New(),
            Fingerprint(),
            CreatedAt);
    }

    private static AvailabilitySlot CreateSlot(
        AppointmentModality modality,
        TimeSpan duration)
    {
        var startsAt = CreatedAt.AddDays(1);
        return AvailabilitySlot.Create(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            startsAt,
            startsAt.Add(duration),
            IanaTimeZone.Create("America/Lima"),
            modality,
            true,
            CreatedAt);
    }

    private static AppointmentRequestFingerprint Fingerprint() =>
        AppointmentRequestFingerprint.Create(new string('a', 64));
}
